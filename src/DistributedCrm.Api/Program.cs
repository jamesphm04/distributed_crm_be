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
    options.UseNpgsql(builder.Configuration.GetConnectionString("DistributedCrm"))
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
    var factory = new ConnectionFactory { HostName = "localhost", Port = 5672 };
    var connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
    RabbitMqReturnRequestQueue.DeclareTopologyAsync(connection).GetAwaiter().GetResult();
    return connection;
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