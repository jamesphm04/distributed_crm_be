using DistributedCrm.Api.Authentication;
using DistributedCrm.Application.Abstractions;
using DistributedCrm.Application.UseCases.ReturnRequests;
using DistributedCrm.Infrastructure.Caching;
using DistributedCrm.Infrastructure.Idempotency;
using DistributedCrm.Infrastructure.Messaging;
using DistributedCrm.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

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
    options.UseNpgsql(builder.Configuration.GetConnectionString("DistributedCrm"),
            npgsqlOptions => npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 5))
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
    var options = ConfigurationOptions.Parse(builder.Configuration.GetConnectionString("Redis")!);
    options.AbortOnConnectFail = false; // don't throw at startup if Redis isn't ready yet - keep retrying in the background
    return ConnectionMultiplexer.Connect(options);
});

// The client connects lazily and retries on its own, so there's no startup retry loop to hand-roll.
// Phase 10 swaps the connection string for AddServiceBusClientWithNamespace + managed identity.
builder.Services.AddAzureClients(clients =>
    clients.AddServiceBusClient(builder.Configuration.GetConnectionString("ServiceBus")));

builder.Services.AddHostedService<OutboxPublisherBackgroundService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // The fallback policy would otherwise lock the API description itself.
    app.MapOpenApi().AllowAnonymous();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();