using DistributedCrm.Application.Contracts;

namespace DistributedCrm.Application.Abstractions;

public interface IReturnRequestCache
{
    Task<ReturnRequestResponse?> GetAsync(Guid id, CancellationToken ct = default);
    Task SetAsync(Guid id, ReturnRequestResponse response, CancellationToken ct = default);
    Task InvalidateAsync(Guid id, CancellationToken ct = default);
}