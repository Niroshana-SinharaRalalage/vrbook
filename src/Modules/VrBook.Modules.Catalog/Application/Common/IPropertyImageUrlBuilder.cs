namespace VrBook.Modules.Catalog.Application.Common;

/// <summary>
/// Resolves an internal blob path (e.g. <c>property-images/{id}/cover.webp</c>) to a
/// publicly fetchable URL. The default implementation prefixes the Blob account URL;
/// production replaces this with a SAS-signing implementation.
/// </summary>
public interface IPropertyImageUrlBuilder
{
    string ToUrl(string blobPath);
}
