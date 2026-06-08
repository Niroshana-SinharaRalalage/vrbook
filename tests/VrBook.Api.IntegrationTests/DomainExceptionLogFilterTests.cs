using FluentAssertions;
using FluentValidation.Results;
using Serilog.Events;
using Serilog.Parsing;
using VrBook.Api.Observability;
using VrBook.Domain.Common;
using Xunit;
using FluentValidationException = FluentValidation.ValidationException;

namespace VrBook.Api.IntegrationTests;

/// <summary>
/// Unit tests for <see cref="DomainExceptionLogFilter"/>. The filter suppresses
/// framework-emitted Error-level logs whose exception is a known domain exception
/// (BusinessRuleViolation, NotFound, Conflict, Forbidden, ValidationException) —
/// these get logged loudly by Microsoft.AspNetCore.Hosting.Diagnostics as 500
/// even though Hellang's ProblemDetailsMiddleware has already remapped the
/// response to 422/404/409/403/400. Our own LoggingBehavior already logs them
/// at Warning level with full context.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DomainExceptionLogFilterTests
{
    private static LogEvent MakeEvent(LogEventLevel level, Exception? ex)
    {
        var template = new MessageTemplateParser().Parse("noise");
        return new LogEvent(
            DateTimeOffset.UtcNow,
            level,
            ex,
            template,
            properties: Array.Empty<LogEventProperty>());
    }

    [Theory]
    [InlineData(nameof(BusinessRuleViolationException))]
    [InlineData(nameof(NotFoundException))]
    [InlineData(nameof(ForbiddenException))]
    [InlineData(nameof(ConflictException))]
    public void Suppresses_error_log_for_known_domain_exception(string exType)
    {
        Exception ex = exType switch
        {
            nameof(BusinessRuleViolationException) => new BusinessRuleViolationException("test.rule", "msg"),
            nameof(NotFoundException) => new NotFoundException("Booking", Guid.NewGuid()),
            nameof(ForbiddenException) => new ForbiddenException("nope"),
            nameof(ConflictException) => new ConflictException("dupe"),
            _ => throw new ArgumentOutOfRangeException(nameof(exType)),
        };
        var evt = MakeEvent(LogEventLevel.Error, ex);

        DomainExceptionLogFilter.ShouldLog(evt).Should().BeFalse();
    }

    [Fact]
    public void Suppresses_error_log_for_fluent_validation_exception()
    {
        var ex = new FluentValidationException(new[]
        {
            new ValidationFailure("Field", "is required"),
        });
        var evt = MakeEvent(LogEventLevel.Error, ex);

        DomainExceptionLogFilter.ShouldLog(evt).Should().BeFalse();
    }

    [Fact]
    public void Keeps_error_log_for_genuine_unhandled_exception()
    {
        var evt = MakeEvent(LogEventLevel.Error, new InvalidOperationException("real bug"));

        DomainExceptionLogFilter.ShouldLog(evt).Should().BeTrue();
    }

    [Fact]
    public void Keeps_error_log_with_no_exception()
    {
        // Framework can log Error-level without an exception (e.g. circuit breaker open).
        var evt = MakeEvent(LogEventLevel.Error, ex: null);

        DomainExceptionLogFilter.ShouldLog(evt).Should().BeTrue();
    }

    [Theory]
    [InlineData(LogEventLevel.Warning)]
    [InlineData(LogEventLevel.Information)]
    [InlineData(LogEventLevel.Debug)]
    public void Keeps_non_error_level_log_even_with_domain_exception(LogEventLevel level)
    {
        // Our LoggingBehavior emits BRV at Warning — must not suppress.
        var evt = MakeEvent(level, new BusinessRuleViolationException("test.rule", "msg"));

        DomainExceptionLogFilter.ShouldLog(evt).Should().BeTrue();
    }
}
