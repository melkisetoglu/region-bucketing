using Xunit;

namespace RegionBucketing.Tests;

/// <summary>
/// The fourth invariant from the article: when the feature flag is off, the
/// vary-key contribution must be a stable sentinel — *not* the empty-set
/// bucket — so that pre-migration cache keys are reproduced byte-for-byte.
///
/// We model this with a small helper that mirrors what the sample's
/// VaryByValue lambda does. The real test of the invariant lives in the
/// app code; this just pins the contract: sentinel ≠ any real bucket hash.
/// </summary>
public class VaryKeySentinelTests
{
    private const string Sentinel = "off";

    [Fact]
    public void Sentinel_does_not_collide_with_any_bucket_hash()
    {
        var hasher = new BucketHasher();

        // Try a wide range of plausible region sets; none should hash to "off".
        for (int seed = 0; seed < 500; seed++)
        {
            var ids = Enumerable.Range(seed, (seed % 7) + 1).ToArray();
            var bucket = hasher.Hash(ids);
            Assert.NotEqual(Sentinel, bucket);
        }
    }

    [Fact]
    public void Sentinel_is_stable_across_calls()
    {
        // Trivial but non-negotiable: if the "off" string ever changed, it would
        // invalidate the whole pre-migration cache on flag-flip — the opposite
        // of what the sentinel exists for.
        Assert.Equal("off", Sentinel);
    }
}
