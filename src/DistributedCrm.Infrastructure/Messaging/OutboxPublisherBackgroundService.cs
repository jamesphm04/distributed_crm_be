using System.Diagnostics;
using DistributedCrm.Application.Abstractions;
using DistributedCrm.Infrastructure.Persistence;
using DistributedCrm.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DistributedCrm.Infrastructure.Messaging;

// Relays rows written by IOutboxStore to Service Bus. The write to Postgres (ReturnRequest + OutboxMessage,
// same DbContext, one SaveChangesAsync) is atomic; this service is what makes the eventual publish durable
// even if Service Bus is unreachable at the moment of creation - it just retries on the next poll.
public class OutboxPublisherBackgroundService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private const int BatchSize = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxPublisherBackgroundService> _logger;

    public OutboxPublisherBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxPublisherBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Outbox publish cycle failed; will retry on next poll.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task PublishPendingAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DistributedCrmDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<IReturnRequestQueue>();

        var pending = await dbContext.OutboxMessages
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var message in pending)
        {
            // The send and the UPDATE run inside this span, so the Service Bus message carries the request's trace
            // to the worker, and the UPDATE is traced (Npgsql skips SQL with no span around it).
            using var activity = StartRelayActivity(message);
            try
            {
                await queue.PublishReturnRequestCreatedAsync(message.ReturnRequestId, ct);
                message.MarkProcessed();
                await dbContext.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                activity?.AddException(ex);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw;
            }
        }
    }

    // Continues the trace of the request that wrote the row. With no stored (or a malformed) trace_parent, TryParse
    // leaves the context empty and the span starts a new trace instead.
    private static Activity? StartRelayActivity(OutboxMessage message)
    {
        ActivityContext.TryParse(message.TraceParent, traceState: null, isRemote: true, out var parentContext);

        var activity = InfrastructureTelemetry.ActivitySource.StartActivity(
            $"outbox relay {message.MessageType}", ActivityKind.Internal, parentContext);
        activity?.SetTag("outbox.message.id", message.Id);
        activity?.SetTag("return_request.id", message.ReturnRequestId);
        // How long the row waited for the relay: the gap between the request's 201 and the publish.
        activity?.SetTag("outbox.delay_ms", (int)(DateTimeOffset.UtcNow - message.CreatedAt).TotalMilliseconds);
        return activity;
    }
}
