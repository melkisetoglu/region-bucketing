namespace RegionBucketing.Sample.Web;

/// <summary>
/// Throwaway in-memory data so the sample runs without a database.
/// In a real deployment, the loader passed to <see cref="RegionIndex"/>
/// would fetch this from EF / Dapper / a config service.
///
/// Note that ZA and BW resolve to the *same* region set — they will share
/// a bucket hash and therefore a single output-cache entry.
/// KE and ZA differ — they get distinct entries.
/// </summary>
public static class DemoData
{
    public static readonly IDictionary<string, IReadOnlyCollection<int>> CountryToRegions
        = new Dictionary<string, IReadOnlyCollection<int>>(StringComparer.OrdinalIgnoreCase)
        {
            // SADC (1) + Africa (2) + Global (7)
            ["ZA"] = new[] { 1, 2, 7 },
            ["BW"] = new[] { 1, 2, 7 },

            // Africa + Global only
            ["KE"] = new[] { 2, 7 },

            // EU (3) + EEA (4) + Global
            ["DE"] = new[] { 3, 4, 7 },
            ["FR"] = new[] { 3, 4, 7 },

            // Americas (5) + Global
            ["US"] = new[] { 5, 7 },
        };

    public static object CatalogFor(string country) => new
    {
        country,
        items = new[]
        {
            new { sku = "SKU-001", title = "Sample item A" },
            new { sku = "SKU-002", title = "Sample item B" },
        }
    };
}
