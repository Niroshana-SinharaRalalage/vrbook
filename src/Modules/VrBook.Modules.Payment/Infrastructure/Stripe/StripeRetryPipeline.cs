using Polly;
using Polly.Retry;
using Stripe;

namespace VrBook.Modules.Payment.Infrastructure.Stripe;

/// <summary>
/// OPS.M.5 §10 best-practice #2 — Polly retry-with-exponential-backoff for
/// <see cref="global::Stripe.StripeException"/> where <c>IsRetriable</c>
/// (transient 5xx, 429). 3 attempts total (1 initial + 2 retries),
/// 250ms / 750ms / 2s backoff. Non-retriable errors fail fast.
/// </summary>
public static class StripeRetryPipeline
{
    /// <summary>Total attempts including the first (so 3 = first + 2 retries).</summary>
    public const int MaxAttempts = 3;

    /// <summary>
    /// Build a fresh pipeline. Polly's stateless pipeline can be reused; this
    /// helper returns the same instance per call shape for simplicity.
    /// </summary>
    public static ResiliencePipeline Build() =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = MaxAttempts - 1,
                ShouldHandle = new PredicateBuilder().Handle<StripeException>(IsRetriable),
                BackoffType = DelayBackoffType.Constant,
                DelayGenerator = static args => ValueTask.FromResult<TimeSpan?>(args.AttemptNumber switch
                {
                    0 => TimeSpan.FromMilliseconds(250),
                    1 => TimeSpan.FromMilliseconds(750),
                    _ => TimeSpan.FromSeconds(2),
                }),
            })
            .Build();

    /// <summary>
    /// Stripe's official retry guidance: retry transient server errors (5xx)
    /// and rate-limit responses (429). Client errors (4xx) are caller bugs —
    /// never retry. <see cref="StripeException"/> exposes the HTTP status code
    /// directly; we map it here instead of digging into the SDK's enum.
    /// </summary>
    private static bool IsRetriable(StripeException ex)
    {
        var code = (int)ex.HttpStatusCode;
        return code == 429 || (code >= 500 && code < 600);
    }
}
