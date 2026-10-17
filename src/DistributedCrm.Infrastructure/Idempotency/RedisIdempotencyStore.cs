using System.Text.Json;
using DistributedCrm.Application.Abstractions;
using DistributedCrm.Application.Contracts;
using DistributedCrm.Application.Exceptions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace DistributedCrm.Infrastructure.Idempotency;

// Idempotency is a guarantee, not an optimisation, so claiming fails closed: if Redis is down the request is refused
// (IdempotencyStoreUnavailableException) rather than created without duplicate protection.
public class RedisIdempotencyStore : IIdempotencyStore
{
    private const string InProgressMarker = "IN_PROGRESS";
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisIdempotencyStore> _logger;

    public RedisIdempotencyStore(IConnectionMultiplexer redis, ILogger<RedisIdempotencyStore> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<IdempotencyClaimResult> TryClaimAsync(string idempotencyKey,
        CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = BuildKey(idempotencyKey);

            // try to claim a new key
            var claimed = await db.StringSetAsync(key, InProgressMarker, Ttl, When.NotExists);
            if (claimed)
            {
                return new IdempotencyClaimResult(IdempotencyClaimStatus.Claimed, null);
            }

            // if not the first one
            var existingValue = await db.StringGetAsync(key);
            if (existingValue == InProgressMarker)
            {
                return new IdempotencyClaimResult(IdempotencyClaimStatus.InProgress, null);
            }

            var response = JsonSerializer.Deserialize<ReturnRequestResponse>((string)existingValue!);

            return new IdempotencyClaimResult(IdempotencyClaimStatus.Completed, response);
        }
        catch (Exception ex) when (IsRedisUnavailable(ex))
        {
            throw new IdempotencyStoreUnavailableException(ex);
        }
    }

    // Runs after the row has committed, so a failure here must not turn a created request into an error.
    // The key stays claimed (IN_PROGRESS), so a retry with it gets 409 instead of creating a duplicate.
    public async Task StoreResultAsync(string idempotencyKey, ReturnRequestResponse response, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = BuildKey(idempotencyKey);
            var serialized = JsonSerializer.Serialize(response);

            await db.StringSetAsync(key, serialized, Ttl);
        }
        catch (Exception ex) when (IsRedisUnavailable(ex))
        {
            _logger.LogWarning(ex,
                "Storing the result for return request {ReturnRequestId} failed; retries with its idempotency key will get 409.",
                response.Id);
        }
    }

    // RedisTimeoutException derives from TimeoutException, not RedisException, so both are needed.
    private static bool IsRedisUnavailable(Exception ex) => ex is RedisException or RedisTimeoutException;

    private static string BuildKey(string idempotencyKey) => $"idempotency:{idempotencyKey}";
}
