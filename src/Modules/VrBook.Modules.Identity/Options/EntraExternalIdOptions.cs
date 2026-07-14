namespace VrBook.Modules.Identity.Options;

/// <summary>
/// Bound from configuration section <c>EntraExternalId</c> (VRB-200). These
/// values wire JwtBearer against the Entra External tenant (ADR-0012). In
/// Staging/Production all three of <see cref="Instance"/>, <see cref="TenantId"/>,
/// <see cref="ClientId"/> are required and fail-fast validated at startup — a
/// missing value must crash the host, never silently boot with token validation
/// unwired (closes gap G5; historically <c>AuthExtensions.cs</c> degraded
/// silently). Development boots without them (dev-loopback carve-out) after a
/// single explicit warning.
/// </summary>
public sealed class EntraExternalIdOptions
{
    public const string SectionName = "EntraExternalId";

    /// <summary>e.g. <c>https://vrbookcid.ciamlogin.com</c>.</summary>
    public string Instance { get; set; } = string.Empty;

    /// <summary>GUID of the External tenant.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary><c>vrbook-api</c> app-registration id (token audience).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Optional issuer-host override used by <c>AuthExtensions</c>.</summary>
    public string TenantIssuerHost { get; set; } = string.Empty;

    /// <summary>Admin sign-in flow name — provided + validated by VRB-209 (gap G7).</summary>
    public string AdminFlowName { get; set; } = string.Empty;

    /// <summary>True when all three required values are present.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Instance) &&
        !string.IsNullOrWhiteSpace(TenantId) &&
        !string.IsNullOrWhiteSpace(ClientId);
}
