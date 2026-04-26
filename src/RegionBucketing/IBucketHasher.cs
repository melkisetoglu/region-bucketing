using System.Collections.Generic;

namespace RegionBucketing
{
    /// <summary>
    /// Produces a stable, fixed-width hash of a region-id set.
    /// Same set (regardless of order or duplicates) → same hash across processes and restarts.
    /// </summary>
    public interface IBucketHasher
    {
        /// <summary>
        /// Hash a region-id set. Order of input is irrelevant; duplicates are collapsed.
        /// </summary>
        string Hash(IEnumerable<int> regionIds);
    }
}
