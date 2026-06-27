namespace VrBook.Domain.Common;

/// <summary>
/// Base for exceptions that signal a domain-rule violation. The API layer translates
/// these to RFC 7807 problem responses via Hellang.Middleware.ProblemDetails.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
    protected DomainException(string message, Exception inner) : base(message, inner) { }
}

public sealed class BusinessRuleViolationException : DomainException
{
    public BusinessRuleViolationException(string rule, string message)
        : base($"{rule}: {message}")
    {
        Rule = rule;
    }

    public string Rule { get; }
}

public sealed class NotFoundException : DomainException
{
    public NotFoundException(string aggregate, object id)
        : base($"{aggregate} '{id}' not found.")
    {
        Aggregate = aggregate;
        AggregateId = id.ToString() ?? string.Empty;
    }

    public string Aggregate { get; }
    public string AggregateId { get; }
}

public sealed class ConflictException : DomainException
{
    public ConflictException(string message) : base(message) { }
}

public class ForbiddenException : DomainException
{
    public ForbiddenException(string message) : base(message) { }
}

/// <summary>
/// Thrown by <c>TenantAuthorizationBehavior</c> when a tenant-scoped command's
/// <c>TenantId</c> does not match <c>ICurrentUser.TenantId</c>. Subclasses
/// <see cref="ForbiddenException"/> so it maps to 403 via the existing RFC 7807
/// pipeline, but carries the two tenant ids for audit + telemetry filtering.
/// Per OPS_M_4_PLAN section 3.3.
/// </summary>
public sealed class CrossTenantAccessException : ForbiddenException
{
    public CrossTenantAccessException(Guid attempted, Guid? actual)
        : base($"Cross-tenant write rejected. Attempted={attempted:D}, actual={actual?.ToString("D") ?? "<null>"}.")
    {
        AttemptedTenantId = attempted;
        ActualTenantId = actual;
    }

    public Guid AttemptedTenantId { get; }
    public Guid? ActualTenantId { get; }
}
