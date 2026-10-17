using System.Text.Json;
using DistributedCrm.Application.Abstractions;
using DistributedCrm.Application.Contracts;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace DistributedCrm.Infrastructure.Caching;

// The cache is an optimisation, so it fails open: when Redis is unavailable a read is a miss (the caller falls back
// to Postgres) and a write or invalidation is skipped. Postgres stays the source of truth; an entry that couldn't be
// invalidated is at most one TTL stale.
public class RedisReturnRequestCache : IReturnRequestCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisReturnRequestCache> _logger;

    public RedisReturnRequestCache(IConnectionMultiplexer redis, ILogger<RedisReturnRequestCache> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<ReturnRequestResponse?> GetAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(BuildKey(id));

            if (value.IsNullOrEmpty)
            {
                return null;
            }

            return JsonSerializer.Deserialize<ReturnRequestResponse>((string)value!);
        }
        catch (Exception ex) when (IsRedisUnavailable(ex))
        {
            _logger.LogWarning(ex, "Cache read for return request {ReturnRequestId} failed; reading from the database.", id);
            return null;
        }
    }

    public async Task SetAsync(Guid id, ReturnRequestResponse response, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var serialized = JsonSerializer.Serialize(response);

            await db.StringSetAsync(BuildKey(id), serialized, Ttl);
        }
        catch (Exception ex) when (IsRedisUnavailable(ex))
        {
            _logger.LogWarning(ex, "Cache write for return request {ReturnRequestId} failed; skipped.", id);
        }
    }

    public async Task InvalidateAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(BuildKey(id));
        }
        catch (Exception ex) when (IsRedisUnavailable(ex))
        {
            _logger.LogWarning(ex,
                "Cache invalidation for return request {ReturnRequestId} failed; the entry may be stale until it expires.", id);
        }
    }

    // RedisTimeoutException derives from TimeoutException, not RedisException, so both are needed.
    private static bool IsRedisUnavailable(Exception ex) => ex is RedisException or RedisTimeoutException;

    private static string BuildKey(Guid id) => $"returnrequest:{id}";
}
