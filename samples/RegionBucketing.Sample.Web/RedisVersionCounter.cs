using StackExchange.Redis;

namespace RegionBucketing.Sample.Web;

/// <summary>
/// Production-shaped <see cref="IVersionCounter"/> backed by a single Redis key.
/// One round-trip per EnsureFresh() call — typically once per request before the
/// version compare short-circuits. INCR is atomic across processes.
/// </summary>
public sealed class RedisVersionCounter : IVersionCounter
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisKey _key;

    public RedisVersionCounter(IConnectionMultiplexer redis, string key = "region:membership:version")
    {
        _redis = redis;
        _key = key;
    }

    public long Current
    {
        get
        {
            var value = _redis.GetDatabase().StringGet(_key);
            return value.IsNullOrEmpty ? 0L : (long)value;
        }
    }

    public long Bump() => _redis.GetDatabase().StringIncrement(_key);
}
