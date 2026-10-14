using DistributedCrm.Application.Abstractions;
using DistributedCrm.Application.UseCases.ReturnRequests;
using DistributedCrm.Infrastructure.Caching;
using DistributedCrm.Infrastructure.Idempotency;
using DistributedCrm.Infrastructure.Messaging;
using DistributedCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<DistributedCrmDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DistributedCrm"),
            npgsqlOptions => npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 5))
        .UseSnakeCaseNamingConvention());

builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IReturnRequestRepository, ReturnRequestRepository>();

builder.Services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
builder.Services.AddSingleton<IReturnRequestCache, RedisReturnRequestCache>();
builder.Services.AddSingleton<IReturnRequestQueue, RabbitMqReturnRequestQueue>();

builder.Services.AddScoped<CreateReturnRequestHandler>();
builder.Services.AddScoped<GetReturnRequestByIdHandler>();
builder.Services.AddScoped<ListReturnRequestsHandler>();
builder.Services.AddScoped<CancelReturnRequestHandler>();

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));

builder.Services.AddSingleton<IConnection>(_ =>
{
    var factory = new ConnectionFactory { HostName = builder.Configuration["RabbitMq:Host"] ?? "localhost", Port = 5672 };

    const int maxAttempts = 10;
    var delay = TimeSpan.FromSeconds(1);
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            var connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
            RabbitMqReturnRequestQueue.DeclareTopologyAsync(connection).GetAwaiter().GetResult();
            return connection;
        }
        catch (Exception) when (attempt < maxAttempts)
        {
            Thread.Sleep(delay);
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
        }
    }
});

var app = builder.Build();

app.Services.GetRequiredService<IConnection>(); // forces the singleton factory to run now

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();