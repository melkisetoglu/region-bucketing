using System;
using System.Collections.Generic;

namespace RegionBucketing
{
    /// <summary>
    /// In-process country → region-id map. Rebuilds itself when <see cref="IVersionCounter"/>
    /// reports a value newer than what's loaded. Thread-safe.
    ///
    /// The index does not reference any data-access library. Callers wire a
    /// <see cref="Loader"/> delegate at startup that knows how to fetch the country↔region
    /// mapping from the real source of truth (EF, Dapper, a JSON file — whatever).
    /// This keeps the library free of database dependencies and avoids circular references.
    /// </summary>
    public sealed class RegionIndex : IRegionIndex
    {
        /// <summary>
        /// Returns the current country → region-ids map. Called once per refresh.
        /// </summary>
        public delegate IDictionary<string, IReadOnlyCollection<int>> Loader();

        private readonly Loader _loader;
        private readonly IVersionCounter _version;
        private readonly IBucketHasher _hasher;
        private readonly object _lock = new object();

        // Sentinel: -1 forces an initial load on the first read.
        private long _loadedVersion = -1;

        // Captured-by-reference snapshots; replaced atomically inside the lock.
        private Dictionary<string, IReadOnlyCollection<int>> _countryToRegions
            = new Dictionary<string, IReadOnlyCollection<int>>(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, string> _countryToBucket
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Stable bucket for unknown countries: hash of the empty region set.
        // Computed lazily so the hasher is exercised once.
        private readonly string _emptySetBucket;

        public RegionIndex(Loader loader, IVersionCounter version, IBucketHasher hasher)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _version = version ?? throw new ArgumentNullException(nameof(version));
            _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
            _emptySetBucket = _hasher.Hash(Array.Empty<int>());
        }

        public IReadOnlyCollection<int> RegionsFor(string countryCode)
        {
            EnsureFresh();
            var snapshot = _countryToRegions; // single read; replacement is atomic
            return snapshot.TryGetValue(countryCode ?? string.Empty, out var set)
                ? set
                : Array.Empty<int>();
        }

        public string BucketFor(string countryCode)
        {
            EnsureFresh();
            var snapshot = _countryToBucket;
            return snapshot.TryGetValue(countryCode ?? string.Empty, out var bucket)
                ? bucket
                : _emptySetBucket;
        }

        public void EnsureFresh()
        {
            var live = _version.Current;
            if (live == _loadedVersion) return;

            lock (_lock)
            {
                if (live == _loadedVersion) return;

                var fresh = _loader() ?? new Dictionary<string, IReadOnlyCollection<int>>();

                var regions = new Dictionary<string, IReadOnlyCollection<int>>(
                    fresh.Count, StringComparer.OrdinalIgnoreCase);
                var buckets = new Dictionary<string, string>(
                    fresh.Count, StringComparer.OrdinalIgnoreCase);

                foreach (var kvp in fresh)
                {
                    regions[kvp.Key] = kvp.Value;
                    buckets[kvp.Key] = _hasher.Hash(kvp.Value);
                }

                // Replace references — readers see a consistent snapshot either way.
                _countryToRegions = regions;
                _countryToBucket = buckets;
                _loadedVersion = live;
            }
        }
    }
}
