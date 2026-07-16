using System.Collections.Concurrent;
using VrBook.Contracts.Interfaces;

namespace VrBook.Api.IntegrationTests.Multitenancy;

/// <summary>
/// VRB-101 — an in-memory <see cref="IBlobStorage"/> for the integration suite,
/// so image upload/delete round-trips without a live Azurite container (keeps
/// the fixture single-container: Postgres only). Registered as a shared
/// singleton in <see cref="TwoTenantApiFixture"/> so tests can assert a blob
/// was written / removed (the no-orphan contract).
/// </summary>
public sealed class InMemoryBlobStorage : IBlobStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _blobs = new();

    private static string Key(string container, string path) => $"{container}/{path}";

    public int Count => _blobs.Count;

    public bool Exists(string container, string path) => _blobs.ContainsKey(Key(container, path));

    public async Task<BlobUploadResult> UploadAsync(
        string container, string path, Stream content, string contentType, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        _blobs[Key(container, path)] = bytes;
        return new BlobUploadResult(container, path, new Uri($"https://fake.blob.local/{Key(container, path)}"), bytes.Length);
    }

    public Task DeleteAsync(string container, string path, CancellationToken ct = default)
    {
        _blobs.TryRemove(Key(container, path), out _);
        return Task.CompletedTask;
    }

    public Uri GetReadSasUri(string container, string path, TimeSpan? ttl = null) =>
        new($"https://fake.blob.local/{Key(container, path)}");

    public Task<Stream> OpenReadAsync(string container, string path, CancellationToken ct = default) =>
        Task.FromResult<Stream>(new MemoryStream(_blobs.TryGetValue(Key(container, path), out var b) ? b : []));
}
