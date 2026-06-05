using System.Globalization;
using System.Text;

namespace VrBook.Modules.Catalog.Domain;

/// <summary>
/// Slug generation. Title -> lowercase, ascii-only, hyphen-delimited.
/// Repository ensures uniqueness by suffixing -2, -3, ... on collision.
/// </summary>
public static class Slug
{
    public static string FromTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var trimmed = title.Trim().ToLowerInvariant();

        // Strip diacritics so "Café Côté" -> "cafe cote".
        var normalised = trimmed.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalised.Length);
        foreach (var ch in normalised)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
            else if (char.IsWhiteSpace(ch) || ch is '-' or '_' or '/' or '.')
            {
                sb.Append('-');
            }
            // Everything else gets dropped.
        }
        var slug = sb.ToString().Normalize(NormalizationForm.FormC);

        // Collapse repeats + trim.
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }
        slug = slug.Trim('-');
        return slug.Length == 0 ? "property" : slug;
    }
}
