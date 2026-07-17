using System.Text.RegularExpressions;

namespace VrBook.Application.Common;

/// <summary>
/// VRB-211 — the audit-action naming convention for product-settings changes:
/// <c>settings.&lt;section&gt;.&lt;verb&gt;</c> (e.g. <c>settings.cancellation.set-model</c>,
/// <c>settings.platform.set-tiers</c>). Every settings command's
/// <see cref="IAuditable.AuditAction"/> is built via <see cref="For"/> so the audit
/// query (<c>GetSettingsChangesQuery</c>) and the "Recent changes" UI panel can filter
/// by the <see cref="Prefix"/> and by section. Mirrors the VRB-203 <c>feature-toggle.set</c>
/// precedent.
/// </summary>
public static partial class SettingsAuditActions
{
    /// <summary>The shared action prefix — every settings audit row starts with this.</summary>
    public const string Prefix = "settings.";

    /// <summary>Builds <c>settings.&lt;section&gt;.&lt;verb&gt;</c>. Section + verb must be
    /// lower-kebab-case (letters, digits, single hyphens), matching the feature-flag
    /// key discipline, so the action space stays queryable + predictable.</summary>
    public static string For(string section, string verb)
    {
        Require(section, nameof(section));
        Require(verb, nameof(verb));
        return $"{Prefix}{section}.{verb}";
    }

    /// <summary>The action prefix that selects every change in a section
    /// (<c>settings.&lt;section&gt;.</c>) — used by the audit query's section filter.</summary>
    public static string SectionPrefix(string section)
    {
        Require(section, nameof(section));
        return $"{Prefix}{section}.";
    }

    private static void Require(string token, string paramName)
    {
        if (string.IsNullOrWhiteSpace(token) || !KebabToken().IsMatch(token))
        {
            throw new ArgumentException(
                $"Settings audit token must be lower-kebab-case (e.g. 'cancellation', 'set-model'); was '{token}'.",
                paramName);
        }
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex KebabToken();
}
