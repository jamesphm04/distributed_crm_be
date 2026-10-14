namespace DistributedCrm.Application.Abstractions;

public interface IOutboxStore
{
    Task EnqueueReturnRequestCreatedAsync(Guid returnRequestId, CancellationToken ct = default);
}
