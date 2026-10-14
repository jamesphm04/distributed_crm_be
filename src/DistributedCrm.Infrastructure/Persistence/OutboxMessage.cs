namespace DistributedCrm.Infrastructure.Persistence;

public class OutboxMessage
{
    public const string ReturnRequestCreatedType = "ReturnRequestCreated";

    public Guid Id { get; private set; }
    public string MessageType { get; private set; } = string.Empty;
    public Guid ReturnRequestId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    private OutboxMessage() {}

    public static OutboxMessage ForReturnRequestCreated(Guid returnRequestId) => new()
    {
        Id = Guid.CreateVersion7(),
        MessageType = ReturnRequestCreatedType,
        ReturnRequestId = returnRequestId,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    public void MarkProcessed() => ProcessedAt = DateTimeOffset.UtcNow;
}
