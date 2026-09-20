namespace DistributedCrm.Application.Abstractions;

public interface IReturnRequestQueue
{
    Task PublishReturnRequestCreatedAsync(Guid returnRequestId, CancellationToken ct = default);
}