using System.Collections.Generic;

namespace RegionBucketing
{
    /// <summary>
    /// In-process country → region-id map with version-aware refresh.
    /// </summary>
    public interface IRegionIndex
    {
        /// <summary>
        /// Region IDs the country belongs to. Empty set if the country is unknown.
        /// </summary>
        IReadOnlyCollection<int> RegionsFor(string countryCode);

        /// <summary>
        /// The current bucket hash for the country. Unknown countries all share the
        /// hash of the empty region set — fine for cache de-duplication.
        /// </summary>
        string BucketFor(string countryCode);

        /// <summary>
        /// Test/diagnostic hook: force a version-counter check on the next access.
        /// </summary>
        void EnsureFresh();
    }
}
