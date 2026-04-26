using Xunit;

namespace RegionBucketing.Tests;

public class BucketHasherTests
{
    private readonly IBucketHasher _hasher = new BucketHasher();

    [Fact]
    public void Same_set_in_different_orders_produces_same_hash()
    {
        Assert.Equal(_hasher.Hash(new[] { 1, 2, 3 }),
                     _hasher.Hash(new[] { 3, 1, 2 }));
    }

    [Fact]
    public void Different_sets_produce_different_hashes()
    {
        Assert.NotEqual(_hasher.Hash(new[] { 1, 2 }),
                        _hasher.Hash(new[] { 1, 2, 3 }));
    }

    [Fact]
    public void Duplicates_in_input_do_not_change_the_hash()
    {
        Assert.Equal(_hasher.Hash(new[] { 1, 2 }),
                     _hasher.Hash(new[] { 1, 1, 2, 2 }));
    }

    [Fact]
    public void Output_is_fixed_width_and_non_empty_for_empty_input()
    {
        Assert.Equal(12, _hasher.Hash(new[] { 1 }).Length);
        Assert.Equal(12, _hasher.Hash(Array.Empty<int>()).Length);
    }

    [Fact]
    public void Hash_is_stable_across_invocations()
    {
        // Stability matters: a process restart must not change the bucket
        // suffix on existing cache entries, otherwise the cache invalidates
        // itself on every deploy.
        var first = _hasher.Hash(new[] { 5, 7, 11 });
        var second = _hasher.Hash(new[] { 11, 7, 5 });
        var third = new BucketHasher().Hash(new[] { 7, 5, 11 });
        Assert.Equal(first, second);
        Assert.Equal(first, third);
    }
}
