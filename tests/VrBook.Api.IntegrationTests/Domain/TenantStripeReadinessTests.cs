using FluentAssertions;
using VrBook.Contracts.Events;
using VrBook.Modules.Identity.Domain;
using Xunit;

namespace VrBook.Api.IntegrationTests.Domain;

/// <summary>
/// Slice OPS.M.5 Step 2 — RED tests pinning the
/// <see cref="Tenant.UpdateStripeAccountReadiness"/> state machine per
/// `docs/OPS_M_5_PLAN.md` §3.8 (D8) + §9 Step 2.
///
/// <para>The state machine:</para>
/// <list type="bullet">
///   <item>PendingOnboarding + (charges, payouts) = (true, true) → Active +
///         raises <see cref="TenantStripeOnboarded"/> AND <see cref="TenantActivated"/>.</item>
///   <item>Active + (charges OR payouts) = false → Suspended with reason
///         <c>stripe_capability_lost</c> + raises <see cref="TenantStripeSuspended"/>.</item>
///   <item>Any state with no actual transition: no events.</item>
///   <item>Suspended cannot auto-re-Activate from flags alone (operator path).</item>
/// </list>
///
/// <para>Runs in Category=Unit; no Docker required.</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class TenantStripeReadinessTests
{
    private static readonly Email AnySupportEmail = new("support@example.com");
    private const string AnySlug = "acme";
    private const string AnyDisplayName = "Acme Holdings";
    private const string AnyStripeAccountId = "acct_test123";

    private static Tenant NewPendingTenantWithStripeAccount()
    {
        var t = Tenant.Create(AnySlug, AnyDisplayName, AnySupportEmail);
        t.SetStripeAccount(AnyStripeAccountId);
        // Drain factory + SetStripeAccount events so tests assert only on the
        // events the readiness method raises.
        t.DequeueEvents();
        return t;
    }

    [Fact]
    public void PendingOnboarding_with_both_flags_true_transitions_to_Active_and_raises_TenantStripeOnboarded_and_TenantActivated()
    {
        var t = NewPendingTenantWithStripeAccount();
        t.Status.Should().Be(Tenant.StatusPendingOnboarding);

        t.UpdateStripeAccountReadiness(chargesEnabled: true, payoutsEnabled: true);

        t.ChargesEnabled.Should().BeTrue();
        t.PayoutsEnabled.Should().BeTrue();
        t.Status.Should().Be(Tenant.StatusActive);
        t.StripeAccountStatus.Should().Be("Active");

        var events = t.DequeueEvents();
        events.Should().Contain(e => e is TenantStripeOnboarded);
        events.OfType<TenantStripeOnboarded>().Single()
            .Should().Match<TenantStripeOnboarded>(e =>
                e.TenantId == t.Id && e.StripeAccountId == AnyStripeAccountId);
        events.Should().Contain(e => e is TenantActivated);
        events.OfType<TenantActivated>().Single()
            .TenantId.Should().Be(t.Id);
    }

    [Fact]
    public void Active_with_charges_lost_transitions_to_Suspended_with_reason_stripe_capability_lost_and_raises_TenantStripeSuspended()
    {
        var t = NewPendingTenantWithStripeAccount();
        t.UpdateStripeAccountReadiness(true, true);
        t.DequeueEvents();

        t.UpdateStripeAccountReadiness(chargesEnabled: false, payoutsEnabled: true);

        t.Status.Should().Be(Tenant.StatusSuspended);
        t.SuspendedReason.Should().Be("stripe_capability_lost");

        var events = t.DequeueEvents();
        events.Should().ContainSingle(e => e is TenantStripeSuspended);
        events.OfType<TenantStripeSuspended>().Single()
            .Should().Match<TenantStripeSuspended>(e =>
                e.TenantId == t.Id
                && e.StripeAccountId == AnyStripeAccountId
                && e.Reason == "stripe_capability_lost");
    }

    [Fact]
    public void Active_with_payouts_lost_transitions_to_Suspended()
    {
        var t = NewPendingTenantWithStripeAccount();
        t.UpdateStripeAccountReadiness(true, true);
        t.DequeueEvents();

        t.UpdateStripeAccountReadiness(true, false);

        t.Status.Should().Be(Tenant.StatusSuspended);
        t.SuspendedReason.Should().Be("stripe_capability_lost");
    }

    [Fact]
    public void PendingOnboarding_with_only_charges_true_remains_PendingOnboarding_no_events()
    {
        var t = NewPendingTenantWithStripeAccount();

        t.UpdateStripeAccountReadiness(true, false);

        t.Status.Should().Be(Tenant.StatusPendingOnboarding);
        t.ChargesEnabled.Should().BeTrue();
        t.PayoutsEnabled.Should().BeFalse();
        t.DequeueEvents().Should().BeEmpty();
    }

    [Fact]
    public void Active_with_both_flags_true_is_no_op_no_events()
    {
        var t = NewPendingTenantWithStripeAccount();
        t.UpdateStripeAccountReadiness(true, true);
        t.DequeueEvents();
        t.Status.Should().Be(Tenant.StatusActive);

        t.UpdateStripeAccountReadiness(true, true);

        t.Status.Should().Be(Tenant.StatusActive);
        t.DequeueEvents().Should().BeEmpty();
    }

    [Fact]
    public void Suspended_cannot_auto_re_Activate_from_flags_alone()
    {
        var t = NewPendingTenantWithStripeAccount();
        t.UpdateStripeAccountReadiness(true, true);
        t.UpdateStripeAccountReadiness(false, true); // → Suspended
        t.DequeueEvents();
        t.Status.Should().Be(Tenant.StatusSuspended);

        t.UpdateStripeAccountReadiness(true, true);

        // Flags update on the aggregate but Status stays Suspended.
        t.ChargesEnabled.Should().BeTrue();
        t.PayoutsEnabled.Should().BeTrue();
        t.Status.Should().Be(Tenant.StatusSuspended,
            "Suspended is an operator-only state to exit; readiness alone does not auto-re-Activate.");
        t.DequeueEvents().Should().BeEmpty();
    }
}
