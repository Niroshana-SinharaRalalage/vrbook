namespace VrBook.Modules.Catalog.Options;

/// <summary>
/// Bound from configuration section <c>Blob</c> (VRB-200). Azure Blob Storage
/// account + container names for property images, message attachments, and the
/// feed cache. <see cref="AccountUrl"/> is the managed-identity endpoint; when
/// set it must be an absolute URL (fail-fast validated). Empty is valid in
/// Development where image URLs are not resolved.
/// </summary>
public sealed class BlobOptions
{
    public const string SectionName = "Blob";

    public string AccountUrl { get; set; } = string.Empty;

    public string PropertyImagesContainer { get; set; } = "property-images";

    public string MessageAttachmentsContainer { get; set; } = "message-attachments";

    public string FeedCacheContainer { get; set; } = "feed-cache";
}
