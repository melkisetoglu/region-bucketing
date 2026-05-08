# Bucket-hashing a set for geo-aware output caches

A small .NET pattern for keeping output caches correct under geo filtering, without exploding cache cardinality and without coordinated invalidation. I wrote about the thinking behind it [on Medium](https://melkisetoglu.medium.com/caching-a-geo-restricted-app-without-the-cache-exploding-4b0c00d1410e).

The library targets **`netstandard2.0`** so it drops into both modern (`net6+`) and legacy (`net48`) consumers. The runnable sample is **.NET 10** ASP.NET Core minimal API using the `OutputCache` middleware. Porting notes for the classic .NET Framework `OutputCache` are at the bottom.

If you want the pattern straight away, jump to [The pattern, in three pieces](#the-pattern-in-three-pieces). The earlier sections explain *why* this approach instead of the obvious alternatives.

## The problem

Your API serves a catalog. Visibility, pricing, and filters depend on which **regions** the customer's country belongs to (SADC, EU, EEA, Americas, Global, …). A given country resolves to a *set* of region IDs.

You'd like to cache the response. Output caching by URL alone is wrong — two customers in different countries will see each other's results. The naive fix is to vary the cache key by country code. That works, but in a system with 200+ ISO countries it sprays the cache, drops your hit rate, and bloats memory. Most countries also share region sets in practice — `ZA` and `BW` both resolve to *{SADC, Africa, Global}* in this project's seed data, and there's no good reason their cache entries should be separate.

The other naive fix is to flush the output cache whenever an admin edits a country↔region mapping. That works once. Do it under load and you've got a stampede.

## Why not the obvious alternatives

**Per-country vary key.** Cardinality blowup, low hit rate, no benefit when many countries map to the same region set.

**Per-(country, version) vary key with a global flush on edit.** Stampede risk; also forces every process to refetch the whole map at the same instant.

**Distributed cache for the resolver state.** The resolver answers "what regions does this country belong to" — a microsecond-scale in-memory dictionary lookup. Round-tripping that to Redis on every request to save the membership lookup costs more than the lookup itself, so it's not worth doing.

**Pub/sub invalidation.** Workable, but you take on delivery semantics, message ordering, missed-message recovery, and the operational cost of running a message bus. The pattern below collapses all of that to a single `INCR` on Redis.

## The pattern, in three pieces

### 1. Hash the *set*, not the country

The cache key suffix is a `BucketHash` over the **sorted, distinct** region-id list:

```csharp
public string Hash(IEnumerable<int> regionIds)
{
    var canonical = string.Join(",", regionIds.Distinct().OrderBy(x => x));
    using var sha = SHA256.Create();
    var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
    return Base32Truncated(bytes, 12); // Crockford alphabet
}
```

Properties that matter:

- **Order- and duplicate-insensitive.** Two countries with the same region set produce the same hash, regardless of how the loader returned them. This is the de-duplication property — `ZA` and `BW` collapse into one cache entry instead of two.
- **Stable across processes and restarts.** The hash is a pure function of the input. Bouncing a process doesn't invalidate cache entries written by a peer or a previous version of itself.
- **Fixed width.** 12 Base32 characters = 60 bits, so accidental collisions only become plausible at around a billion distinct buckets — irrelevant when your real region sets number in the dozens.
- **Readable in logs.** We use the Crockford Base32 alphabet (skips `I/L/O/U` so the hash is hard to misread). Hex would also work; it just takes more characters for the same entropy.

The full implementation is in [`src/RegionBucketing/BucketHasher.cs`](src/RegionBucketing/BucketHasher.cs).

### 2. Wire it as an output-cache vary-key

In .NET 10, the `OutputCache` middleware exposes `VaryByValue`, which contributes a `(key, value)` pair into the cache key for each request:

```csharp
app.MapGet("/catalog", (HttpContext ctx, IRegionIndex idx) => /* … */)
   .CacheOutput(policy => policy
       .Expire(TimeSpan.FromMinutes(5))
       .SetVaryByQuery("country")
       .VaryByValue(ctx =>
       {
           if (!useRegionBasedGeo)
               return new KeyValuePair<string, string>("geo", "off");

           var resolver = ctx.RequestServices.GetRequiredService<IRegionIndex>();
           var country = ctx.Request.Query["country"].ToString();
           return new KeyValuePair<string, string>("geo", resolver.BucketFor(country));
       }));
```

A few details worth flagging:

- **The lambda runs once per request, before the cache lookup.** `IRegionIndex.BucketFor` is an in-process dictionary read after a single counter compare — microseconds.
- **`useRegionBasedGeo = false` returns the sentinel `"off"`**, *not* the empty-set bucket hash. The sentinel must never collide with a real hash so that flag-off cache keys are byte-identical to the pre-migration baseline. Flipping the flag should not invalidate the world. The sample reads this flag from `appsettings.json` (`"UseRegionBasedGeo"`) at startup; in a .NET Framework app you'd read it from `Web.config`'s `appSettings` or wherever your app keeps feature flags.
- **Use the *effective* country, not the account country**, if you have a "travel grace" concept. The vary-key naturally separates entries for the traveller's effective set vs. the home set.

### 3. Bump a version counter — don't broadcast

The third piece is the cheapest. The country↔region mapping lives in the database. When an admin edits it, the admin write also `INCR`s a single Redis key — `region:membership:version`.

Each process holds an in-memory snapshot of the country→regions map and the version it loaded. Every request, on the way to computing `BucketFor(country)`, the index does:

```csharp
public void EnsureFresh()
{
    var live = _version.Current;     // one Redis GET; cheap
    if (live == _loadedVersion) return;
    lock (_lock)
    {
        if (live == _loadedVersion) return;
        var fresh = _loader();        // EF / Dapper / whatever
        _countryToRegions = Rebuild(fresh);
        _countryToBucket  = HashAll(fresh, _hasher);
        _loadedVersion = live;
    }
}
```

Why this works without coordination:

- **Eventually consistent without a message bus.** Every process converges on the latest version on its own. Missed messages can't happen because there are no messages.
- **No stampede.** Old cache entries age out naturally as their TTLs expire. Nothing flushes anything. New requests start producing the new bucket hash; old entries simply stop being looked up.
- **Self-healing on cold start.** A fresh process loads the current version on its first request. There's no setup step it can miss.
- **The Redis GET is cheap.** A single round-trip per request that short-circuits to a no-op almost every time. If even that worries you, cache `Current` for one second — the pattern still works.

The full index implementation is in [`src/RegionBucketing/RegionIndex.cs`](src/RegionBucketing/RegionIndex.cs).

## The four invariants the tests pin

1. **Same set → same hash.** `BW` and `ZA` collide on purpose. ([`BucketHasherTests`](tests/RegionBucketing.Tests/BucketHasherTests.cs))
2. **Different sets → different hashes.** `ZA` and `KE` separate cleanly. ([`RegionIndexTests`](tests/RegionBucketing.Tests/RegionIndexTests.cs))
3. **Bump triggers rebuild on next read.** No bump → stale value preserved (intentionally). ([`RegionIndexTests`](tests/RegionBucketing.Tests/RegionIndexTests.cs))
4. **Flag-off sentinel never collides with a real hash.** ([`VaryKeySentinelTests`](tests/RegionBucketing.Tests/VaryKeySentinelTests.cs))

If you ever change the canonicalisation rule, the hash width, or the alphabet, the existing cache entries become invalid — every consumer's keys shift on the same deploy. That's usually fine; just be aware of it.

## Trade-offs and where this doesn't fit

- **Read-after-write for the editor.** Between admin edit and counter bump propagation, *one process tick* may serve the old value to the editor refreshing their own page. Acceptable in nearly all cases; if not, route admin reads around the cache.
- **High write rate on the membership table.** If admins edit country↔region mappings dozens of times per minute, every process rebuilds its in-memory map on every request. The map is small, but you're paying for it. Throttle the bump or batch admin writes.
- **Hashes are not human-debuggable.** A 12-char Base32 string isn't going to tell you which countries it represents. Add a diagnostics endpoint (the sample includes one at `/diagnostics/bucket?country=ZA`).
- **Region cardinality.** This pattern is built for *dozens* of distinct region sets. If your customers each have unique region sets — e.g. per-customer entitlements — bucket-hashing degenerates back to per-customer caching. Use a different cache strategy for that workload.
- **Keep the resolver itself in memory, not Redis.** It's tempting to centralise the country↔regions map in Redis, but the serialization round-trip dominates the lookup it's meant to save. The map is small; let each process hold its own copy.

## Porting notes — .NET Framework 4.8 `OutputCache`

If you're applying this pattern in a `net48` MVC + Web API codebase, there are two integration points:

**MVC** — override `GetVaryByCustomString` in `Global.asax.cs`:

```csharp
public override string GetVaryByCustomString(HttpContext context, string custom)
{
    var baseKey = base.GetVaryByCustomString(context, custom);
    var country = ResolveCountry(context); // your existing logic
    var bucket = useRegionBasedGeo
        ? regionIndex.BucketFor(country)
        : "off";
    return baseKey + "|geo=" + bucket;
}
```

**Web API** — implement `ICacheKeyGenerator` (or extend the one in `WebApi.OutputCache`) and append the same `|geo=<bucket>` suffix to the generated key. Same idea, different mechanism.

Everything else — the hasher, the index, the version counter — is identical. The library targets `netstandard2.0` precisely so the same DLL serves both stacks.

## Repo layout

```
src/RegionBucketing/                — netstandard2.0 library (the pattern)
samples/RegionBucketing.Sample.Web/ — net10.0 minimal API + OutputCache + Redis counter
tests/RegionBucketing.Tests/        — net10.0 xUnit; the four invariants
```

## Running locally

```bash
dotnet restore
dotnet test
dotnet run --project samples/RegionBucketing.Sample.Web
```

Then:

```bash
# ZA and BW share a bucket (same region set)
curl 'http://localhost:5080/diagnostics/bucket?country=ZA'
curl 'http://localhost:5080/diagnostics/bucket?country=BW'

# KE has a different bucket
curl 'http://localhost:5080/diagnostics/bucket?country=KE'

# Two GETs to /catalog?country=ZA hit the cache (same servedAt)
curl 'http://localhost:5080/catalog?country=ZA'
curl 'http://localhost:5080/catalog?country=ZA'

# Bump the counter — the next /catalog call rebuilds the in-memory map
curl -X POST 'http://localhost:5080/admin/regions/bump'

# Exercise flag-off behaviour: set UseRegionBasedGeo=false in appsettings.json
# (or pass it as an env var) and restart. The vary-key contribution becomes "off"
# regardless of country, matching the pre-migration cache key shape.
UseRegionBasedGeo=false dotnet run --project samples/RegionBucketing.Sample.Web
```

That's the whole pattern.
