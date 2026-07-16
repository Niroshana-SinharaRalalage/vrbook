using System.ComponentModel.DataAnnotations;

namespace VrBook.Modules.Catalog.Options;

/// <summary>
/// VRB-101 — property-image upload policy, bound from configuration section
/// <c>Catalog:Images</c>. Standardized machinery, values tunable per-env.
/// </summary>
public sealed class CatalogImageOptions
{
    public const string SectionName = "Catalog:Images";

    /// <summary>Maximum accepted upload size in megabytes.</summary>
    [Range(1, 50)]
    public int MaxSizeMb { get; set; } = 10;

    /// <summary>Accepted MIME types. Anything else is rejected 422.</summary>
    public string[] AllowedContentTypes { get; set; } = ["image/jpeg", "image/png", "image/webp"];

    /// <summary>
    /// Read-SAS TTL in minutes. Implemented on the blob adapter for future
    /// private-container use; VRB-101 ships public-read URLs (owner ruling
    /// 2026-07-16) so reads do not currently mint SAS.
    /// </summary>
    [Range(1, 60)]
    public int SasTtlMinutes { get; set; } = 10;

    public long MaxSizeBytes => (long)MaxSizeMb * 1024L * 1024L;
}
