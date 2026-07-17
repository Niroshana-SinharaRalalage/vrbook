namespace VrBook.Contracts.Dtos;

/// <summary>
/// VRB-211 — one row of the settings "Recent changes" panel, projected from the audit
/// log. <paramref name="Actor"/> is the resolved actor display name (email/name, or
/// "system"). <paramref name="Action"/> is the <c>settings.&lt;section&gt;.&lt;verb&gt;</c>
/// action. <paramref name="Before"/>/<paramref name="After"/> are the redacted JSON
/// payloads (secret-valued keys masked at write time). <paramref name="At"/> is when it
/// occurred.
/// </summary>
public sealed record SettingsChangeDto(
    string Actor,
    string Action,
    string? Before,
    string? After,
    DateTimeOffset At);
