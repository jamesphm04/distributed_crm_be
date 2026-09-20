using DistributedCrm.Application.Contracts;

namespace DistributedCrm.Application.Abstractions;

public interface IIdempotencyStore
{
    Task<IdempotencyClaimResult> TryClaimAsync(string idempotencyKey, CancellationToken ct = default);

    Task StoreResultAsync(string idempotencyKey, ReturnRequestResponse response, CancellationToken ct = default);
}