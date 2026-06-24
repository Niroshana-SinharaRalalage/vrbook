using FluentValidation;

namespace VrBook.Modules.Reports.Application.Common;

/// <summary>
/// Shared envelope for the four report queries. Validated by
/// <see cref="ReportRangeQueryValidator{T}"/> via the generic base check.
/// </summary>
public interface IReportRangeQuery
{
    DateOnly From { get; }
    DateOnly To { get; }
    Guid? PropertyId { get; }
}

public abstract class ReportRangeQueryValidator<T> : AbstractValidator<T> where T : IReportRangeQuery
{
    protected ReportRangeQueryValidator()
    {
        RuleFor(q => q.From).NotEqual(default(DateOnly))
            .WithMessage("From is required.");
        RuleFor(q => q.To).GreaterThanOrEqualTo(q => q.From)
            .WithMessage("To must be on or after From.");
        RuleFor(q => q)
            .Must(q => q.To.DayNumber - q.From.DayNumber <= 366)
            .WithMessage("Range cannot exceed 366 days.");
    }
}
