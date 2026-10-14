using Azure.Messaging.ServiceBus;
using DistributedCrm.Application.Abstractions;

namespace DistributedCrm.Infrastructure.Messaging;

public sealed class ServiceBusReturnRequestQueue : IReturnRequestQueue, IAsyncDisposable
{
    // Defined outside the app: distributed_crm_infra/servicebus/Config.json locally, Bicep in Azure.
    // The worker reads from the same name - the one coupling point between the two repos.
    public const string QueueName = "return-requests";

    private readonly ServiceBusSender _sender;

    public ServiceBusReturnRequestQueue(ServiceBusClient client)
    {
        // Senders are thread-safe and meant to live as long as the app; this class is a singleton.
        _sender = client.CreateSender(QueueName);
    }

    public async Task PublishReturnRequestCreatedAsync(Guid returnRequestId, CancellationToken ct = default)
    {
        var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(new { returnRequestId }))
        {
            // Duplicate-detection key. There's exactly one "created" event per return request, so if the
            // outbox relay crashes between sending and MarkProcessed() and re-sends, Service Bus drops the repeat.
            MessageId = $"return-request-created:{returnRequestId}",
            ContentType = "application/json",
            Subject = "ReturnRequestCreated",
        };

        await _sender.SendMessageAsync(message, ct);
    }

    public ValueTask DisposeAsync() => _sender.DisposeAsync();
}
