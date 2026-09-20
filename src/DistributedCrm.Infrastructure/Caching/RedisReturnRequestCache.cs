using System.Text.Json;
using DistributedCrm.Application.Abstractions;
using DistributedCrm.Application.Contracts;
using StackExchange.Redis;

namespace DistributedCrm.Infrastructure.Caching;

public class RedisReturnRequestCache : IReturnRequestCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private readonly IConnectionMultiplexer _redis;

    public RedisReturnRequestCache(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<ReturnRequestResponse?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(BuildKey(id));

        if (value.IsNullOrEmpty)
        {
            return null;
        }

        return JsonSerializer.Deserialize<ReturnRequestResponse>((string)value!);
    }

    public async Task SetAsync(Guid id, ReturnRequestResponse response, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var serialized = JsonSerializer.Serialize(response);

        await db.StringSetAsync(BuildKey(id), serialized, Ttl);
    }

    public async Task InvalidateAsync(Guid id, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(BuildKey(id));
    }

    private static string BuildKey(Guid id) => $"returnrequest:{id}";
}