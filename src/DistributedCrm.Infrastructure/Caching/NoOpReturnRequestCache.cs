using DistributedCrm.Application.Abstractions;
using DistributedCrm.Application.Contracts;

namespace DistributedCrm.Infrastructure.Caching;

// Used when Caching:Enabled is false: every read is a miss, so it goes to Postgres, and nothing is written to Redis.
// It's the kill switch for a misbehaving cache, and the baseline for measuring what the cache is worth. Entries left
// in Redis are not invalidated while it's on, so turning the cache back on can serve an entry up to one TTL stale,
// the same guarantee as a failed invalidation.
public class NoOpReturnRequestCache : IReturnRequestCache
{
    public Task<ReturnRequestResponse?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult<ReturnRequestResponse?>(null);

    public Task SetAsync(Guid id, ReturnRequestResponse response, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task InvalidateAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
}
