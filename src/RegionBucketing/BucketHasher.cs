using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RegionBucketing
{
    /// <summary>
    /// SHA-256 over the canonical (sorted, distinct, comma-separated) region-id list,
    /// truncated to a 12-character Crockford-style Base32 string.
    ///
    /// 12 chars × 5 bits = 60 bits of entropy → birthday collision becomes plausible
    /// at roughly 2^30 ≈ 10^9 distinct buckets. For region-set cardinalities measured
    /// in the dozens or hundreds, collision risk is effectively zero.
    /// </summary>
    public sealed class BucketHasher : IBucketHasher
    {
        // Crockford Base32 — no I, L, O, U. Easier to eyeball in logs than RFC4648.
        private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        private const int OutputChars = 12;

        public string Hash(IEnumerable<int> regionIds)
        {
            if (regionIds == null) throw new ArgumentNullException(nameof(regionIds));

            // Canonicalise: sort, dedupe, comma-join. Stable across .NET versions.
            var canonical = string.Join(",", regionIds.Distinct().OrderBy(x => x));

            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                return Base32Truncated(bytes, OutputChars);
            }
        }

        private static string Base32Truncated(byte[] bytes, int chars)
        {
            var sb = new StringBuilder(chars);
            int buffer = 0;
            int bits = 0;
            foreach (var b in bytes)
            {
                buffer = (buffer << 8) | b;
                bits += 8;
                while (bits >= 5 && sb.Length < chars)
                {
                    bits -= 5;
                    sb.Append(Alphabet[(buffer >> bits) & 0x1F]);
                }
                if (sb.Length >= chars) break;
            }
            return sb.ToString();
        }
    }
}
