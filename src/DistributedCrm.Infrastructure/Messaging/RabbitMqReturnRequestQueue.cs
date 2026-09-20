using System.Text.Json;
using DistributedCrm.Application.Abstractions;
using RabbitMQ.Client;

namespace DistributedCrm.Infrastructure.Messaging;

public class RabbitMqReturnRequestQueue : IReturnRequestQueue
{
    private const string ExchangeName = "return-requests"; // RECEIVEs new messages
    private const string RetryExchangeName = "return-requests.retry"; // RECEIVEs failed (nacked) messages
    private const string DlxExchangeName = "return-requests.dlx"; // RECEIVEs messages that exhausted all retries

    private const string QueueName = "return-requests.process"; // CONTAINs processing messages
    private const string RetryQueueName = "return-requests.retry"; // CONTAINs nacked messages, awaiting for TTL to expire -> return-requests exchange -> return-requests.process -> retry + 1
    private const string DlqName = "return-requests.dlq"; // CONTAINS failed message resting place — a human looks at this

    private const string RoutingKey = "created";
    private const int RetryTtlMilliseconds = 30_000;

    private readonly IConnection _connection;

    public RabbitMqReturnRequestQueue(IConnection connection)
    {
        _connection = connection;
    }

    public async Task PublishReturnRequestCreatedAsync(Guid returnRequestId, CancellationToken ct = default)
    {
        await using var channel = await _connection.CreateChannelAsync(cancellationToken: ct);

        var payload = JsonSerializer.SerializeToUtf8Bytes(new { returnRequestId });

        await channel.BasicPublishAsync(
            exchange: ExchangeName,
            routingKey: RoutingKey,
            mandatory: false,
            basicProperties: new BasicProperties { Persistent = true },
            body: payload,
            cancellationToken: ct);
    }

    public static async Task DeclareTopologyAsync(IConnection connection, CancellationToken ct = default)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        
        // main exchange + queue: dead-letters to the retry exchange on nack
        await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Direct, durable: true, cancellationToken: ct);
        await channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = RetryExchangeName
            },
            cancellationToken: ct);
        await channel.QueueBindAsync(QueueName, ExchangeName, RoutingKey, cancellationToken: ct);
        
        // retry exchange + queue: holds message for TTL, then dead-letters back to main 
        await channel.ExchangeDeclareAsync(RetryExchangeName, ExchangeType.Direct, durable: true, cancellationToken: ct);
        await channel.QueueDeclareAsync(
            queue: RetryQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = ExchangeName,
                ["x-message-ttl"] = RetryTtlMilliseconds
            },
            cancellationToken: ct);
        await channel.QueueBindAsync(RetryQueueName, RetryExchangeName, RoutingKey, cancellationToken: ct);
        
        // dead-letter exchange + queue: terminal park after max retries
        await channel.ExchangeDeclareAsync(DlxExchangeName, ExchangeType.Direct, durable: true, cancellationToken: ct);
        await channel.QueueDeclareAsync(
            queue: DlqName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: ct);
        await channel.QueueBindAsync(DlqName, DlxExchangeName, RoutingKey, cancellationToken: ct);
    }
}