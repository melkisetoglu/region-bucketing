using RegionBucketing;
using RegionBucketing.Sample.Web;

var builder = WebApplication.CreateBuilder(args);

// --- Composition root ----------------------------------------------------
//
// In production the loader hits your real datasource (EF, Dapper, etc.).
// Here we use a static map. We wire InMemoryVersionCounter for the demo;
// swap it for RedisVersionCounter in any deployment with > 1 process.

var hasher = new BucketHasher();
var version = new InMemoryVersionCounter();
var index = new RegionIndex(
    loader: () => DemoData.CountryToRegions,
    version: version,
    hasher: hasher);

builder.Services.AddSingleton<IBucketHasher>(hasher);
builder.Services.AddSingleton<IVersionCounter>(version);
builder.Services.AddSingleton<IRegionIndex>(index);

builder.Services.AddOutputCache();

var app = builder.Build();
app.UseOutputCache();

// --- The pattern ---------------------------------------------------------
//
// VaryByValue contributes a (key, value) pair into the cache key for each
// cached request. We compute the bucket hash of the request's country and
// contribute it. Two countries that resolve to the same region set produce
// the same hash → they share one cache entry. Different region sets → they
// get distinct entries. No cardinality blow-up.
//
// Flag-off behaviour: when UseRegionBasedGeo is false, contribute a stable
// sentinel ("off") so vary-keys are byte-identical to the pre-migration
// baseline. This avoids a cache stampede when the flag flips.
//
// The lambda runs once per request — if the version counter hasn't moved,
// IRegionIndex returns from its in-process snapshot in microseconds.

// Feature flag from configuration. Read at startup so the sentinel branch
// in the VaryByValue lambda is genuinely reachable at runtime.
var useRegionBasedGeo = app.Configuration.GetValue<bool>("UseRegionBasedGeo", true);

app.MapGet("/catalog", (HttpContext ctx, IRegionIndex idx) =>
{
    var country = ctx.Request.Query["country"].ToString();
    return Results.Ok(new
    {
        country,
        bucket = idx.BucketFor(country),
        catalog = DemoData.CatalogFor(country),
        servedAt = DateTimeOffset.UtcNow
    });
})
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

// Admin endpoint: bumping the counter triggers a rebuild on every process's
// next read. Old vary-keys age out of the output cache naturally.
app.MapPost("/admin/regions/bump", (IVersionCounter v) =>
    Results.Ok(new { version = v.Bump() }));

// Diagnostics: see what bucket a country resolves to right now.
app.MapGet("/diagnostics/bucket", (string country, IRegionIndex idx) =>
    Results.Ok(new { country, bucket = idx.BucketFor(country), regions = idx.RegionsFor(country) }));

app.Run();
