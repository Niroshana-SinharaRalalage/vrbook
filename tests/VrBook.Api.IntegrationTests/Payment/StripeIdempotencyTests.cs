using FluentAssertions;
using VrBook.Modules.Payment.Infrastructure.Stripe;
using Xunit;

namespace VrBook.Api.IntegrationTests.Payment;

/// <summary>
/// Slice OPS.M.5 Step 3 RED — pins the per-call idempotency-key format per
/// `docs/OPS_M_5_PLAN.md` §10 best-practice #1.
///
/// <para>Stripe deduplicates writes by idempotency key for ~24h. If two
/// requests reach Stripe with the same key, only the first runs. The keys
/// below must be stable across retries within an operation but unique
/// across operations — wrong format silently drops legitimate writes or
/// re-runs cancelled ones.</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class StripeIdempotencyTests
{
    [Fact]
    public void ForOnboarding_uses_tenant_onboarding_prefix_with_tenant_id_in_D_format()
    {
        var tenantId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
        StripeIdempotency.ForOnboarding(tenantId)
            .Should().Be("tenant-onboarding:aaaaaaaa-1111-2222-3333-444444444444");
    }

    [Fact]
    public void ForPaymentIntent_uses_booking_prefix_with_booking_id_in_N_format()
    {
        var bookingId = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");
        StripeIdempotency.ForPaymentIntent(bookingId)
            .Should().Be("booking:bbbbbbbb111122223333444444444444:pi");
    }

    [Fact]
    public void ForRefund_uses_refund_prefix_with_refund_id_in_N_format()
    {
        var refundId = Guid.Parse("cccccccc-1111-2222-3333-444444444444");
        StripeIdempotency.ForRefund(refundId)
            .Should().Be("refund:cccccccc111122223333444444444444");
    }
}
