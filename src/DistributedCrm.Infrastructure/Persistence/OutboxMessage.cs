using System.Diagnostics;

namespace DistributedCrm.Infrastructure.Persistence;

public class OutboxMessage
{
    public const string ReturnRequestCreatedType = "ReturnRequestCreated";

    // A W3C traceparent is always "00-<32 hex trace id>-<16 hex span id>-<2 hex flags>".
    public const int TraceParentMaxLength = 55;

    public Guid Id { get; private set; }
    public string MessageType { get; private set; } = string.Empty;
    public Guid ReturnRequestId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    // The trace context of the request that wrote this row, so the relay can continue that trace. HTTP headers and
    // Service Bus properties carry it automatically; a table only does if it has a column for it.
    // Null for rows written outside any trace (or before this column existed): the relay then starts a new trace.
    public string? TraceParent { get; private set; }

    private OutboxMessage() {}

    public static OutboxMessage ForReturnRequestCreated(Guid returnRequestId) => new()
    {
        Id = Guid.CreateVersion7(),
        MessageType = ReturnRequestCreatedType,
        ReturnRequestId = returnRequestId,
        CreatedAt = DateTimeOffset.UtcNow,
        TraceParent = Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity ? activity.Id : null,
    };

    public void MarkProcessed() => ProcessedAt = DateTimeOffset.UtcNow;
}
