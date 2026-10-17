using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using DistributedCrm.Api.Authentication;
using DistributedCrm.Api.Configuration;
using DistributedCrm.Api.Telemetry;
using DistributedCrm.Application.Abstractions;
using DistributedCrm.Application.UseCases.ReturnRequests;
using DistributedCrm.Infrastructure.Caching;
using DistributedCrm.Infrastructure.HealthChecks;
using DistributedCrm.Infrastructure.Idempotency;
using DistributedCrm.Infrastructure.Messaging;
using DistributedCrm.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Read up front so a missing value stops startup with the variable's name, instead of failing on a request.
var postgresConnectionString = builder.Configuration.GetRequiredConnectionString("DistributedCrm");
var redisConnectionString = builder.Configuration.GetRequiredConnectionString("Redis");
var serviceBusConnectionString = builder.Configuration.GetRequiredConnectionString("ServiceBus");

builder.AddTelemetry();

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddOptions<GoogleAuthOptions>()
    .Bind(builder.Configuration.GetSection(GoogleAuthOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// Configured through the options pipeline so the client ID comes from the validated GoogleAuthOptions,
// not a second raw read of IConfiguration.
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<GoogleAuthOptions>>((options, googleAuth) =>
    {
        // Discovery document + signing keys (JWKS) are fetched from here and refreshed when Google rotates keys.
        options.Authority = "https://accounts.google.com";

        // Keep Google's claim names ("email", "sub") instead of renaming them to long schema URIs.
        options.MapInboundClaims = false;

        // Google issues ID tokens with either spelling of its issuer.
        options.TokenValidationParameters.ValidIssuers = ["https://accounts.google.com", "accounts.google.com"];
        options.TokenValidationParameters.ValidAudience = googleAuth.Value.ClientId;
    });

// Singleton: it only holds the allowlist, built once from options.
builder.Services.AddSingleton<IAuthorizationHandler, AllowedEmailHandler>();

var allowedStaffPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
    .RequireAuthenticatedUser()
    .AddRequirements(new AllowedEmailRequirement())
    .Build();

// Default policy: what a bare [Authorize] means.
// Fallback policy: what applies to any endpoint with no [Authorize]/[AllowAnonymous] at all - secure by default.
builder.Services.AddAuthorizationBuilder()
    .SetDefaultPolicy(allowedStaffPolicy)
    .SetFallbackPolicy(allowedStaffPolicy);

builder.Services.AddDbContext<DistributedCrmDbContext>(options =>
    options.UseNpgsql(postgresConnectionString, npgsqlOptions => npgsqlOptions
            .EnableRetryOnFailure(maxRetryCount: 5)
            // Trace SQL only inside an existing span: the outbox relay's once-a-second poll would otherwise be a
            // one-span trace every second. Step 11 gives the relay a parent span, so its real work shows up again.
            // Batches are filtered too because EF Core's SaveChanges sends one. Physical opens can't be filtered,
            // and the pool reopening idle connections every 5 minutes made root spans, so they're off; the
            // Npgsql pool metrics still show connection churn.
            .ConfigureDataSource(dataSource => dataSource.ConfigureTracing(tracing => tracing
                .ConfigureCommandFilter(_ => Activity.Current is not null)
                .ConfigureBatchFilter(_ => Activity.Current is not null)
                .EnablePhysicalOpenTracing(false))))
        .UseSnakeCaseNamingConvention());

builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IReturnRequestRepository, ReturnRequestRepository>();
builder.Services.AddScoped<IOutboxStore, OutboxStore>();

builder.Services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
builder.Services.AddSingleton<IReturnRequestCache, RedisReturnRequestCache>();
builder.Services.AddSingleton<IReturnRequestQueue, ServiceBusReturnRequestQueue>();

builder.Services.AddScoped<CreateReturnRequestHandler>();
builder.Services.AddScoped<GetReturnRequestByIdHandler>();
builder.Services.AddScoped<ListReturnRequestsHandler>();
builder.Services.AddScoped<CancelReturnRequestHandler>();

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var options = ConfigurationOptions.Parse(redisConnectionString);
    options.AbortOnConnectFail = false; // don't throw at startup if Redis isn't ready yet - keep retrying in the background
    // Without a connection, throw at once instead of queueing the command until the 5 s timeout. Callers decide
    // what a Redis failure means (the cache falls back to Postgres, idempotency returns 503), so waiting buys nothing.
    options.BacklogPolicy = BacklogPolicy.FailFast;
    return ConnectionMultiplexer.Connect(options);
});

// The client connects lazily and retries on its own, so there's no startup retry loop to hand-roll.
// Phase 10 swaps the connection string for AddServiceBusClientWithNamespace + managed identity.
builder.Services.AddAzureClients(clients =>
    clients.AddServiceBusClient(serviceBusConnectionString));

// Singleton: one receiver link for the app's lifetime instead of a new one per probe.
builder.Services.AddSingleton(sp => new ServiceBusQueueHealthCheck(
    sp.GetRequiredService<ServiceBusClient>(),
    ServiceBusReturnRequestQueue.QueueName));

// Dependency checks run only on /health/ready, never on /health/live.
const string readyTag = "ready";
builder.Services.AddHealthChecks()
    .AddDbContextCheck<DistributedCrmDbContext>("postgres", tags: [readyTag])
    .AddCheck<RedisHealthCheck>("redis", tags: [readyTag])
    .AddCheck<ServiceBusQueueHealthCheck>("servicebus", tags: [readyTag]);

// A hung dependency fails the probe instead of hanging it. Set here because AddDbContextCheck takes no timeout.
builder.Services.PostConfigure<HealthCheckServiceOptions>(options =>
{
    foreach (var registration in options.Registrations)
    {
        registration.Timeout = TimeSpan.FromSeconds(5);
    }
});

// Container Apps terminates HTTPS at its ingress and forwards plain HTTP with the original scheme and client IP
// in X-Forwarded-* headers. The ingress is the only way in, so its (unknown, changing) address is trusted.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddHostedService<OutboxPublisherBackgroundService>();

var app = builder.Build();

// First, so everything after it sees the client's real scheme and IP.
app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // The fallback policy would otherwise lock the API description itself.
    app.MapOpenApi().AllowAnonymous();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Platform probes carry no token. The response is only "Healthy"/"Unhealthy"; failure details go to the log.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = r => r.Tags.Contains(readyTag) })
    .AllowAnonymous();

app.Run();