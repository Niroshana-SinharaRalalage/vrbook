using VrBook.Contracts.Interfaces;

namespace VrBook.Infrastructure.Storage;

/// <summary>
/// VRB-101 — fallback <see cref="IBlobStorage"/> for hosts with no blob backend
/// configured (bare dev / some test fixtures). It is constructible so the DI
/// container's build-time validation passes even though image handlers always
/// depend on <see cref="IBlobStorage"/>, but throws a clear error if actually
/// used — production always binds <c>AzureBlobStorage</c>, and integration
/// fixtures that exercise uploads register an in-memory fake.
/// </summary>
internal sealed class UnconfiguredBlobStorage : IBlobStorage
{
    private static InvalidOperationException NotConfigured() => new(
        "Blob storage is not configured. Set Blob:AccountUrl (managed identity) or a " +
        "Blob connection string (Azurite) to enable property image uploads.");

    public Task<BlobUploadResult> UploadAsync(
        string container, string path, Stream content, string contentType, CancellationToken ct = default) =>
        throw NotConfigured();

    public Task DeleteAsync(string container, string path, CancellationToken ct = default) =>
        throw NotConfigured();

    public Uri GetReadSasUri(string container, string path, TimeSpan? ttl = null) => throw NotConfigured();

    public Task<Stream> OpenReadAsync(string container, string path, CancellationToken ct = default) =>
        throw NotConfigured();
}
