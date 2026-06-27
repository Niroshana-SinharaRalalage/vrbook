using FluentAssertions;
using Stripe;
using VrBook.Modules.Payment.Infrastructure.Stripe;
using Xunit;

namespace VrBook.Api.IntegrationTests.Payment;

/// <summary>
/// Slice OPS.M.5 Step 3 RED — pins the Polly retry behavior per
/// `docs/OPS_M_5_PLAN.md` §10 best-practice #2.
///
/// <para>The pipeline must retry transient <see cref="StripeException"/>
/// (where <c>IsRetriable</c> is true — Stripe surfaces this on 5xx / 429)
/// 3 attempts total (1 initial + 2 retries), and pass through non-retriable
/// errors immediately. Wrong retry behavior on a non-idempotent write would
/// double-charge cards (we mitigate with §10 #1 idempotency keys but the
/// retry policy is still required to be conservative).</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class StripeRetryPipelineTests
{
    [Fact]
    public async Task Transient_StripeException_retries_three_times_then_succeeds()
    {
        var attempts = 0;
        var pipeline = StripeRetryPipeline.Build();

        await pipeline.ExecuteAsync(async _ =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw NewTransientStripeException();
            }
            await Task.CompletedTask;
        });

        attempts.Should().Be(3, "first attempt + 2 retries = 3 total per OPS.M.5 §10 #2.");
    }

    [Fact]
    public async Task Transient_StripeException_giving_up_after_three_attempts_throws()
    {
        var attempts = 0;
        var pipeline = StripeRetryPipeline.Build();

        Func<Task> act = async () => await pipeline.ExecuteAsync(_ =>
        {
            attempts++;
            throw NewTransientStripeException();
        });

        await act.Should().ThrowAsync<StripeException>();
        attempts.Should().Be(3,
            "after 3 attempts the pipeline gives up and rethrows the last exception.");
    }

    [Fact]
    public async Task Non_retriable_StripeException_does_not_retry()
    {
        var attempts = 0;
        var pipeline = StripeRetryPipeline.Build();

        Func<Task> act = async () => await pipeline.ExecuteAsync(_ =>
        {
            attempts++;
            throw NewNonRetriableStripeException();
        });

        await act.Should().ThrowAsync<StripeException>();
        attempts.Should().Be(1, "non-retriable errors fail fast.");
    }

    /// <summary>4xx server error with IsRetriable=true marker — mimics Stripe 429 / 5xx.</summary>
    private static StripeException NewTransientStripeException()
    {
        var stripeError = new StripeError { Type = "api_error", Code = "lock_timeout" };
        return new StripeException(System.Net.HttpStatusCode.ServiceUnavailable, stripeError, "Transient");
    }

    /// <summary>Invalid-request-style error — Stripe surfaces IsRetriable=false.</summary>
    private static StripeException NewNonRetriableStripeException()
    {
        var stripeError = new StripeError { Type = "invalid_request_error", Code = "parameter_missing" };
        return new StripeException(System.Net.HttpStatusCode.BadRequest, stripeError, "Bad request");
    }
}
