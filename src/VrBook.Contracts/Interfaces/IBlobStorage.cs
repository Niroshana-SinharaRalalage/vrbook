namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Thin wrapper over Azure.Storage.Blobs. Used by Catalog (property images) and
/// Messaging (message attachments). Local dev hits Azurite.
/// </summary>
public interface IBlobStorage
{
    Task<BlobUploadResult> UploadAsync(
        string container,
        string path,
        Stream content,
        string contentType,
        CancellationToken ct = default);

    Task DeleteAsync(string container, string path, CancellationToken ct = default);

    /// <summary>Generate a read-only SAS URL with a short TTL (default 10 minutes).</summary>
    Uri GetReadSasUri(string container, string path, TimeSpan? ttl = null);

    Task<Stream> OpenReadAsync(string container, string path, CancellationToken ct = default);
}

public sealed record BlobUploadResult(string Container, string Path, Uri Uri, long SizeBytes);
