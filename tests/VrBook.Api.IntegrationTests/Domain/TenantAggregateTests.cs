using FluentAssertions;
using VrBook.Contracts.Events;
using VrBook.Modules.Identity.Domain;
using Xunit;

namespace VrBook.Api.IntegrationTests.Domain;

/// <summary>
/// OPS.M.1 — unit tests for Tenant aggregate invariants per `docs/OPS_M_1_PLAN.md` §4 Step 1.
/// Status transitions, factory args, fee bounds, Stripe wiring methods.
/// Runs in the Category=Unit step of CI; no Docker required.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TenantAggregateTests
{
    private static readonly Email AnySupportEmail = new("support@example.com");
    private const string AnySlug = "acme";
    private const string AnyDisplayName = "Acme Holdings";

    [Fact]
    public void Create_succeeds_with_valid_args_and_raises_TenantCreated()
    {
        var tenant = Tenant.Create(AnySlug, AnyDisplayName, AnySupportEmail);

        tenant.Slug.Should().Be(AnySlug);
        tenant.DisplayName.Should().Be(AnyDisplayName);
        tenant.SupportEmail.Should().Be(AnySupportEmail);
        tenant.Status.Should().Be(Tenant.StatusPendingOnboarding);
        tenant.DefaultCurrency.Should().Be("USD");
        tenant.DefaultTimezone.Should().Be("UTC");
        tenant.PlatformFeeBps.Should().Be(1500);
        tenant.StripeAccountId.Should().BeNull();
        tenant.SuspendedReason.Should().BeNull();

        var events = tenant.DequeueEvents();
        events.Should().ContainSingle()
            .Which.Should().BeOfType<TenantCreated>()
            .Which.Should().Match<TenantCreated>(e => e.TenantId == tenant.Id);
    }

    [Fact]
    public void Create_normalises_slug_to_lowercase_and_trims_display_name()
    {
        var tenant = Tenant.Create("  ACME  ", "  Acme Holdings  ", AnySupportEmail);
        tenant.Slug.Should().Be("acme");
        tenant.DisplayName.Should().Be("Acme Holdings");
    }

    [Fact]
    public void Create_normalises_currency_to_uppercase()
    {
        var tenant = Tenant.Create(AnySlug, AnyDisplayName, AnySupportEmail, defaultCurrency: "usd");
        tenant.DefaultCurrency.Should().Be("USD");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_rejects_blank_slug(string? slug)
    {
        var act = () => Tenant.Create(slug!, AnyDisplayName, AnySupportEmail);
        act.Should().Throw<ArgumentException>().WithParameterName("slug");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_rejects_blank_display_name(string? name)
    {
        var act = () => Tenant.Create(AnySlug, name!, AnySupportEmail);
        act.Should().Throw<ArgumentException>().WithParameterName("displayName");
    }

    [Fact]
    public void Create_rejects_null_support_email()
    {
        var act = () => Tenant.Create(AnySlug, AnyDisplayName, supportEmail: null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("supportEmail");
    }

    [Theory]
    [InlineData("US")]
    [InlineData("USDD")]
    public void Create_rejects_currency_not_three_chars(string currency)
    {
        var act = () => Tenant.Create(AnySlug, AnyDisplayName, AnySupportEmail, defaultCurrency: currency);
        act.Should().Throw<ArgumentException>().WithParameterName("defaultCurrency");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10_001)]
    public void Create_rejects_fee_outside_zero_to_ten_thousand_bps(int bps)
    {
        var act = () => Tenant.Create(AnySlug, AnyDisplayName, AnySupportEmail, platformFeeBps: bps);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("platformFeeBps");
    }

    [Fact]
    public void Activate_from_PendingOnboarding_transitions_and_raises_event()
    {
        var tenant = Tenant.Create(AnySlug, AnyDisplayName, AnySupportEmail);
        _ = tenant.DequeueEvents();

        tenant.Activate();

        tenant.Status.Should().Be(Tenant.StatusActive);
        tenant.DequeueEvents().Should().ContainSingle().Which.Should().BeOfType<TenantActivated>();
    }

    [Fact]
    public void Activate_from_non_PendingOnboarding_throws()
    {
        var tenant = Tenant.Create(AnySlug, AnyDisplayName, AnySupportEmail);
        tenant.Activate();
        _ = tenant.DequeueEvents();

        var act = () => tenant.Activate();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Suspend_from_Active_transitions_and_persists_reason()
    {
        var tenant = Tenant.Create(AnySlug, AnyDisplayName, AnySupportEmail);
        tenant.Activate();
        _ = tenant.DequeueEvents();
        var actor = Guid.NewGuid();

        tenant.Suspend(reason: "Stripe charges_enabled=false for >7 days", actor);

        tenant.Status.Should().Be(Tenant.StatusSuspended);
        tenant.SuspendedReason.Should().Be("Stripe charges_enabled=false for >7 days");
        var evt = tenant.DequeueEvents().Should().ContainSingle()
            .Which.Should().BeOfType<TenantSuspended>().Subject;
        evt.ActorId.Should().Be(actor);
        evt.Reason.Should().Be("Stripe charges_enabled=false for >7 days");
    }

    [Fact]
    public void Suspend_from_non_Active_throws()
    {
        var tenant = Tenant.Create(AnySlug, AnyDisplayName, AnySupportEmail);

        var act = () => tenant.Suspend("any reason", Guid.NewGuid());
        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Suspend_rejects_blank_reason(string? reason)
    {
        var tenant = Tenant.Create(AnySlug, AnyDisplayName, AnySupportEmail);
        tenant.Activate();

        var act = () => tenant.Suspend(reason!, Guid.NewGuid());
        act.Should().Throw<ArgumentException>().WithParameterName("reason");
    }

    [Fact]
    public void Reactivate_from_Suspended_clears_reason_and_returns_to_Active()
    {
        var tenant = Tenant.Create(AnySlug, AnyDisplayName, AnySupportEmail);
        tenant.Activate();
        tenant.Suspend("temp", Guid.NewGuid());
        _ = tenant.DequeueEvents();

        tenant.Reactivate();

        tenant.Status.Should().Be(Tenant.StatusActive);
        tenant.SuspendedReason.Should().BeNull();
        tenant.DequeueEvents().Should().ContainSingle().Which.Should().BeOfType<TenantActivated>();
    }

    [Fact]
    public void Reactivate_from_non_Suspended_throws()
    {
        var tenant = Tenant.Create(AnySlug, AnyDisplayName, AnySupportEmail);
        var act = () => tenant.Reactivate();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Close_from_any_status_transitions_and_raises_event_once()
    {
        var tenant = Tenant.Create(AnySlug, AnyDisplayName, AnySupportEmail);
        tenant.Activate();
        _ = tenant.DequeueEvents();

        tenant.Close();
        tenant.Status.Should().Be(Tenant.StatusClosed);
        tenant.DequeueEvents().Should().ContainSingle().Which.Should().BeOfType<TenantClosed>();

        tenant.Close();
        tenant.DequeueEvents().Should().BeEmpty("re-closing a closed tenant is a no-op");
    }

    [Fact]
    public void SetPlatformFeeBps_accepts_zero_and_ten_thousand()
    {
        var tenant = Tenant.Create(AnySlug, AnyDisplayName, AnySupportEmail);
        tenant.SetPlatformFeeBps(0);
        tenant.PlatformFeeBps.Should().Be(0);
        tenant.SetPlatformFeeBps(10_000);
        tenant.PlatformFeeBps.Should().Be(10_000);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10_001)]
    public void SetPlatformFeeBps_rejects_out_of_range(int bps)
    {
        var tenant = Tenant.Create(AnySlug, AnyDisplayName, AnySupportEmail);
        var act = () => tenant.SetPlatformFeeBps(bps);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("bps");
    }

    [Fact]
    public void SetStripeAccount_stores_account_id_trimmed()
    {
        var tenant = Tenant.Create(AnySlug, AnyDisplayName, AnySupportEmail);
        tenant.SetStripeAccount("  acct_1ABC  ");
        tenant.StripeAccountId.Should().Be("acct_1ABC");
    }

    [Fact]
    public void UpdateSupportEmail_replaces_email()
    {
        var tenant = Tenant.Create(AnySlug, AnyDisplayName, AnySupportEmail);
        var newEmail = new Email("ops@example.com");
        tenant.UpdateSupportEmail(newEmail);
        tenant.SupportEmail.Should().Be(newEmail);
    }
}
