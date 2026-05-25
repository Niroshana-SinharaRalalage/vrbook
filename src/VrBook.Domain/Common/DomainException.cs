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

public sealed class ForbiddenException : DomainException
{
    public ForbiddenException(string message) : base(message) { }
}
