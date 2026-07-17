using VrBook.Contracts.Enums;

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

/// <summary>VRB-216 — the platform-global cancellation tier schedule (PlatformAdmin
/// settings). <c>UpgradePricePct</c> is the RefundableUpgrade price as % of subtotal.</summary>
public sealed record GlobalCancellationTiersDto(
    int FirstTierDays,
    int SecondTierDays,
    int MiddleTierRefundPct,
    int FinalCutoffHours,
    int UpgradePricePct,
    int Version,
    string? LastChangedBy,
    DateTimeOffset? LastChangedAt);

/// <summary>VRB-215 — a property's cancellation-model selection (tenant-admin settings).
/// The host picks the model only; the tier schedule + upgrade % are platform-set and
/// echoed read-only in <c>ResolvedTiers</c> for the "what the guest gets" preview.</summary>
public sealed record PropertyCancellationSettingsDto(
    Guid PropertyId,
    CancellationModel Model,
    GlobalCancellationTiersDto ResolvedTiers,
    string? LastChangedBy,
    DateTimeOffset? LastChangedAt);

/// <summary>VRB-216 — platform fee configuration: the default bps + per-tenant overrides
/// (PlatformAdmin settings). Hosts see their effective fee % + net (Q4).</summary>
public sealed record PlatformFeeConfigDto(int DefaultBps, IReadOnlyList<TenantFeeOverrideDto> Overrides);

public sealed record TenantFeeOverrideDto(Guid TenantId, int FeeBps);

/// <summary>VRB-216 — platform tax posture (PlatformAdmin settings): marketplace-facilitator
/// flag + per-state enablement roster (Q25). Engine is PAY VRB-103; this is the posture only.</summary>
public sealed record TaxPostureDto(
    bool FacilitatorActive,
    IReadOnlyDictionary<string, bool> PerStateEnabled);
