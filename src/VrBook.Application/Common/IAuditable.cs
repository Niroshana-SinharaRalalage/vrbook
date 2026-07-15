namespace VrBook.Application.Common;

/// <summary>
/// Marker for MediatR requests that should be written to the audit log on success.
/// Commands implementing this are recorded by the Identity <c>AuditLogBehavior</c>
/// (registered as a global pipeline behavior). PII is JSON-serialised but the redaction
/// policy (proposal §14.5) is applied at write time.
///
/// <para>VRB-203 — promoted from the Identity module to the shared <c>VrBook.Application</c>
/// assembly so any module's command can be auditable without coupling to Identity
/// (the modular-monolith boundary forbids module→module references).</para>
/// </summary>
public interface IAuditable
{
    /// <summary>The human-readable action name (e.g. "user.update-profile").</summary>
    string AuditAction { get; }

    /// <summary>Type name of the target aggregate (e.g. "User"); null for system actions.</summary>
    string? AuditTargetType => GetType().Name;

    /// <summary>Stable id of the target aggregate; null for system actions or pre-create.</summary>
    string? AuditTargetId => null;
}
