using System.Text.Json;
using DistributedCrm.Application.Abstractions;
using DistributedCrm.Application.Contracts;
using StackExchange.Redis;

namespace DistributedCrm.Infrastructure.Idempotency;

public class RedisIdempotencyStore : IIdempotencyStore
{
    private const string InProgressMarker = "IN_PROGRESS";
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    private readonly IConnectionMultiplexer _redis;

    public RedisIdempotencyStore(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<IdempotencyClaimResult> TryClaimAsync(string idempotencyKey,
        CancellationToken ct = default)
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

    public async Task StoreResultAsync(string idempotencyKey, ReturnRequestResponse response, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = BuildKey(idempotencyKey);
        var serialized = JsonSerializer.Serialize(response);

        await db.StringSetAsync(key, serialized, Ttl);
    }

    private static string BuildKey(string idempotencyKey) => $"idempotency:{idempotencyKey}";
}