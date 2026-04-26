using Xunit;

namespace RegionBucketing.Tests;

public class RegionIndexTests
{
    private static RegionIndex Build(
        IDictionary<string, IReadOnlyCollection<int>> data,
        out InMemoryVersionCounter counter)
    {
        counter = new InMemoryVersionCounter();
        var localCounter = counter;
        var localData = data;
        return new RegionIndex(
            loader: () => localData,
            version: localCounter,
            hasher: new BucketHasher());
    }

    [Fact]
    public void Bump_triggers_rebuild_on_next_read()
    {
        var data = new Dictionary<string, IReadOnlyCollection<int>>
        {
            ["ZA"] = new[] { 1, 2 }
        };

        var index = Build(data, out var counter);
        var before = index.BucketFor("ZA");

        // Source-of-truth changes; admin bumps the counter.
        data["ZA"] = new[] { 1, 2, 3 };
        counter.Bump();

        var after = index.BucketFor("ZA");
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Without_bump_index_keeps_serving_stale_value()
    {
        // This is the *intended* behaviour: between admin edit and counter bump,
        // each process serves what it has loaded. The bump is the contract.
        var data = new Dictionary<string, IReadOnlyCollection<int>>
        {
            ["ZA"] = new[] { 1, 2 }
        };

        var index = Build(data, out _);
        var before = index.BucketFor("ZA");

        data["ZA"] = new[] { 1, 2, 3 }; // mutated, but no Bump()
        var after = index.BucketFor("ZA");

        Assert.Equal(before, after);
    }

    [Fact]
    public void Same_region_set_across_countries_yields_same_bucket()
    {
        var data = new Dictionary<string, IReadOnlyCollection<int>>
        {
            ["ZA"] = new[] { 1, 2, 7 },
            ["BW"] = new[] { 7, 1, 2 } // intentionally a different order
        };

        var index = Build(data, out _);

        Assert.Equal(index.BucketFor("ZA"), index.BucketFor("BW"));
    }

    [Fact]
    public void Different_region_sets_yield_different_buckets()
    {
        var data = new Dictionary<string, IReadOnlyCollection<int>>
        {
            ["ZA"] = new[] { 1, 2, 7 },
            ["KE"] = new[] { 2, 7 }
        };

        var index = Build(data, out _);

        Assert.NotEqual(index.BucketFor("ZA"), index.BucketFor("KE"));
    }

    [Fact]
    public void Unknown_country_returns_stable_empty_set_bucket()
    {
        var index = Build(new Dictionary<string, IReadOnlyCollection<int>>(), out _);

        var a = index.BucketFor("XX");
        var b = index.BucketFor("YY");

        Assert.Equal(a, b);
        Assert.Equal(12, a.Length);
    }
}
