namespace VrBook.Modules.Identity.Domain;

/// <summary>
/// Append-only audit row written by the AuditLogBehavior pipeline. PII is redacted
/// at write time per proposal §14.5. Lives in the <c>identity</c> schema because A1
/// owns the pipeline behavior that writes it; admin reads it via a query handler.
/// </summary>
public sealed class AuditLogEntry
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Tenant the actor was scoped to (currentUser.TenantId). Nullable forever
    /// per OPS_M_3_PLAN §1.7 — Super Admin actions and anonymous login-flow
    /// requests have no tenant. Target-tenant joins for "who modified tenant X"
    /// happen at read time in OPS.M.8.
    /// </summary>
    public Guid? TenantId { get; private set; }

    public Guid? ActorUserId { get; private set; }
    public string ActorRole { get; private set; } = "anonymous";
    public string Action { get; private set; } = default!;
    public string? TargetType { get; private set; }
    public string? TargetId { get; private set; }
    public string? Before { get; private set; }   // jsonb
    public string? After { get; private set; }    // jsonb
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string? TraceId { get; private set; }

    private AuditLogEntry() { }

    public static AuditLogEntry Record(
        Guid? actorUserId,
        string actorRole,
        string action,
        string? targetType,
        string? targetId,
        string? beforeJson,
        string? afterJson,
        string? ip,
        string? userAgent,
        string? traceId,
        Guid? tenantId = null) =>
        new()
        {
            TenantId = tenantId,
            ActorUserId = actorUserId,
            ActorRole = actorRole,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Before = beforeJson,
            After = afterJson,
            IpAddress = ip,
            UserAgent = userAgent,
            TraceId = traceId,
        };
}
