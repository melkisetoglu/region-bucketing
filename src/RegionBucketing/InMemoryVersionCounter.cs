using System.Threading;

namespace RegionBucketing
{
    /// <summary>
    /// Single-process counter. Useful for tests and single-instance deployments.
    /// In a real multi-process deployment use a shared store (Redis, etc.) so that
    /// every process observes the same bumps.
    /// </summary>
    public sealed class InMemoryVersionCounter : IVersionCounter
    {
        private long _value;

        public long Current => Interlocked.Read(ref _value);

        public long Bump() => Interlocked.Increment(ref _value);
    }
}
