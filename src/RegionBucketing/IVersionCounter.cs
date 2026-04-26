namespace RegionBucketing
{
    /// <summary>
    /// A monotonically increasing counter that is bumped whenever the source-of-truth
    /// region-membership data changes (e.g. an admin edits a country↔region mapping).
    ///
    /// Reads must be cheap. A network round-trip to Redis is fine: each process compares
    /// the live counter against its loaded version on each request and short-circuits
    /// almost every time.
    /// </summary>
    public interface IVersionCounter
    {
        /// <summary>The current counter value. Reads should be cheap.</summary>
        long Current { get; }

        /// <summary>Atomically increment the counter and return the new value.</summary>
        long Bump();
    }
}
