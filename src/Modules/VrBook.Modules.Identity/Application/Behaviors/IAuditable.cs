namespace VrBook.Modules.Identity.Application.Behaviors;

/// <summary>
/// Marker for MediatR requests that should be written to the audit log on success.
/// Commands implementing this are recorded by <see cref="AuditLogBehavior{TRequest,TResponse}"/>.
/// PII is JSON-serialised but redaction policy (proposal §14.5) is applied at write time.
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
