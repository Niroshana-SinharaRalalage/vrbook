using Polly;

namespace VrBook.Modules.Payment.Infrastructure.Stripe;

/// <summary>
/// OPS.M.5 §10 best-practice #2 — Polly retry-with-exponential-backoff for
/// <see cref="global::Stripe.StripeException"/> where <c>IsRetriable</c>
/// (transient 5xx, 429). 3 attempts, 250ms / 750ms / 2s.
///
/// <para>Step 3 RED: <see cref="Build"/> returns <see cref="ResiliencePipeline.Empty"/>
/// so <c>StripeRetryPipelineTests</c> fail (no retry happens). GREEN replaces
/// with the real pipeline.</para>
/// </summary>
public static class StripeRetryPipeline
{
    /// <summary>Total attempts including the first (so 3 = first + 2 retries).</summary>
    public const int MaxAttempts = 3;

    /// <summary>
    /// Build a fresh pipeline. Use only one instance per logical operation so
    /// the retry counter doesn't bleed across calls.
    /// </summary>
    public static ResiliencePipeline Build() => ResiliencePipeline.Empty;
}
