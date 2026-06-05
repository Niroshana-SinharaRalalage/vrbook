using Microsoft.Extensions.Configuration;
using VrBook.Modules.Catalog.Application.Common;

namespace VrBook.Modules.Catalog.Infrastructure.Storage;

/// <summary>
/// Builds a public-ish URL by prefixing <c>Blob:AccountUrl</c>. Good enough for
/// staging where blobs are public read. Replace with a SAS-token signer for prod.
/// </summary>
internal sealed class PropertyImageUrlBuilder(IConfiguration configuration) : IPropertyImageUrlBuilder
{
    private readonly string _baseUrl = (configuration["Blob:AccountUrl"] ?? string.Empty).TrimEnd('/');
    private readonly string _container = configuration["Blob:PropertyImagesContainer"] ?? "property-images";

    public string ToUrl(string blobPath)
    {
        if (string.IsNullOrWhiteSpace(blobPath))
        {
            return string.Empty;
        }
        // If the path already looks fully-qualified, leave it alone.
        if (blobPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            blobPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return blobPath;
        }
        return $"{_baseUrl}/{_container}/{blobPath.TrimStart('/')}";
    }
}
