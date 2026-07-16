using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using VrBook.Contracts.Interfaces;

namespace VrBook.Infrastructure.Storage;

/// <summary>
/// VRB-101 — the concrete <see cref="IBlobStorage"/> (the interface previously
/// had no implementation). Wraps a single <see cref="BlobServiceClient"/> that
/// <c>DependencyInjection</c> constructs from either a managed-identity account
/// URL (staging/prod) or a connection string (Azurite for local dev). Container
/// existence is assumed (provisioned by <c>infra/modules/storage.bicep</c>).
/// </summary>
internal sealed class AzureBlobStorage(BlobServiceClient client) : IBlobStorage
{
    public async Task<BlobUploadResult> UploadAsync(
        string container,
        string path,
        Stream content,
        string contentType,
        CancellationToken ct = default)
    {
        var blob = client.GetBlobContainerClient(container).GetBlobClient(path);
        // No overwrite: image paths embed a fresh GUID so a collision is a bug.
        await blob.UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            ct);
        var size = content.CanSeek ? content.Length : 0L;
        return new BlobUploadResult(container, path, blob.Uri, size);
    }

    public async Task DeleteAsync(string container, string path, CancellationToken ct = default) =>
        await client.GetBlobContainerClient(container).GetBlobClient(path).DeleteIfExistsAsync(cancellationToken: ct);

    public Uri GetReadSasUri(string container, string path, TimeSpan? ttl = null)
    {
        var blob = client.GetBlobContainerClient(container).GetBlobClient(path);
        // Service SAS is only mintable when the client holds a shared key
        // (connection-string auth). Under managed identity a user-delegation SAS
        // is async and out of this sync contract; VRB-101 ships public-read URLs
        // (owner ruling) so reads never reach here — return the plain blob URI.
        if (blob.CanGenerateSasUri)
        {
            return blob.GenerateSasUri(
                BlobSasPermissions.Read,
                DateTimeOffset.UtcNow.Add(ttl ?? TimeSpan.FromMinutes(10)));
        }
        return blob.Uri;
    }

    public async Task<Stream> OpenReadAsync(string container, string path, CancellationToken ct = default) =>
        await client.GetBlobContainerClient(container).GetBlobClient(path).OpenReadAsync(cancellationToken: ct);
}
