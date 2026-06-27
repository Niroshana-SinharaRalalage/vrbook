using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VrBook.Contracts.Common;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Payment.Application.Commands;
using VrBook.Modules.Payment.Domain;
using VrBook.Modules.Payment.Infrastructure.Persistence;
using VrBook.Modules.Payment.Infrastructure.Stripe;
using Xunit;

namespace VrBook.Api.IntegrationTests.Payment;

/// <summary>
/// Slice OPS.M.5 Step 6 — pins the rewritten
/// <c>CreatePaymentIntentForBookingHandler</c> per
/// `docs/OPS_M_5_PLAN.md` §3.5 + §3.6 + §3.4 + §9 Step 6.
///
/// <para>The handler must:
/// <list type="number">
///   <item>Resolve the tenant's Stripe context via <see cref="ITenantStripeContextLookup"/>
///         (no raw SQL — the OPS.M.3 fallback is deleted).</item>
///   <item>Throw <c>payment.connect_account_missing</c> when the tenant has no
///         Stripe account (modulo a staging-only AllowPlatformFallback flag).</item>
///   <item>Route via Connect destination charge with the proportional
///         application-fee amount (banker's rounding via
///         <see cref="StripeFeeCalculator"/>).</item>
/// </list>
/// </para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class CreatePaymentIntentForBookingHandlerTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid BookingX = Guid.Parse("00000000-bbbb-0000-0000-000000000001");

    [Fact]
    public async Task Throws_payment_connect_account_missing_when_tenant_has_no_StripeAccountId()
    {
        var (handler, gateway, lookup, repo) = NewHandler(AllowPlatformFallback: false);
        gateway.IsConfigured.Returns(true);
        lookup.GetAsync(TenantA, Arg.Any<CancellationToken>())
            .Returns(new TenantStripeContext(TenantA, StripeAccountId: null, PlatformFeeBps: 1500, DefaultCurrency: "USD"));

        Func<Task> act = () => handler.Handle(
            new CreatePaymentIntentForBookingCommand(BookingX, new Money(100m, "USD"), TenantA),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<BusinessRuleViolationException>();
        ex.Which.Rule.Should().Be("payment.connect_account_missing");
        await gateway.DidNotReceiveWithAnyArgs().CreatePaymentIntentAsync(
            default, default!, default!, default, default!, default, default);
    }

    [Fact]
    public async Task Throws_when_lookup_returns_null()
    {
        var (handler, gateway, lookup, _) = NewHandler();
        gateway.IsConfigured.Returns(true);
        lookup.GetAsync(TenantA, Arg.Any<CancellationToken>())
            .Returns((TenantStripeContext?)null);

        Func<Task> act = () => handler.Handle(
            new CreatePaymentIntentForBookingCommand(BookingX, new Money(100m, "USD"), TenantA),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<BusinessRuleViolationException>();
        ex.Which.Rule.Should().Be("payment.tenant_context_missing");
    }

    [Fact]
    public async Task Routes_via_Connect_with_destinationAccountId_and_proportional_fee_when_tenant_has_StripeAccount()
    {
        var (handler, gateway, lookup, _) = NewHandler();
        gateway.IsConfigured.Returns(true);
        lookup.GetAsync(TenantA, Arg.Any<CancellationToken>())
            .Returns(new TenantStripeContext(TenantA, StripeAccountId: "acct_test_xyz", PlatformFeeBps: 1500, DefaultCurrency: "USD"));
        gateway.CreatePaymentIntentAsync(
                Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(), Arg.Any<string>(), Arg.Any<long>(),
                Arg.Any<CancellationToken>())
            .Returns(new StripeIntentCreated("pi_new", "sec_new", PaymentStatus.RequiresPaymentMethod));

        await handler.Handle(
            new CreatePaymentIntentForBookingCommand(BookingX, new Money(100m, "USD"), TenantA),
            CancellationToken.None);

        await gateway.Received(1).CreatePaymentIntentAsync(
            amount: 100m,
            currency: "USD",
            idempotencyKey: $"booking:{BookingX:N}:pi",
            metadata: Arg.Is<IDictionary<string, string>>(m =>
                m["booking_id"] == BookingX.ToString("D") &&
                m["tenant_id"] == TenantA.ToString("D")),
            destinationAccountId: "acct_test_xyz",
            applicationFeeAmount: 1500L,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Staging_flag_AllowPlatformFallback_true_routes_via_platform_when_no_StripeAccount()
    {
        var (handler, gateway, lookup, _) = NewHandler(AllowPlatformFallback: true);
        gateway.IsConfigured.Returns(true);
        lookup.GetAsync(TenantA, Arg.Any<CancellationToken>())
            .Returns(new TenantStripeContext(TenantA, StripeAccountId: null, PlatformFeeBps: 1500, DefaultCurrency: "USD"));
        gateway.CreatePaymentIntentAsync(
                Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(new StripeIntentCreated("pi_new", "sec_new", PaymentStatus.RequiresPaymentMethod));

        await handler.Handle(
            new CreatePaymentIntentForBookingCommand(BookingX, new Money(100m, "USD"), TenantA),
            CancellationToken.None);

        // Asserts the legacy platform path was taken (no destination/fee args).
        await gateway.Received(1).CreatePaymentIntentAsync(
            Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<IDictionary<string, string>>(), Arg.Any<CancellationToken>());
        await gateway.DidNotReceive().CreatePaymentIntentAsync(
            Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<IDictionary<string, string>>(), Arg.Any<string>(), Arg.Any<long>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_null_when_Stripe_not_configured()
    {
        var (handler, gateway, _, _) = NewHandler();
        gateway.IsConfigured.Returns(false);

        var result = await handler.Handle(
            new CreatePaymentIntentForBookingCommand(BookingX, new Money(100m, "USD"), TenantA),
            CancellationToken.None);

        result.Should().BeNull();
    }

    private static (
        CreatePaymentIntentForBookingHandler handler,
        IStripeGateway gateway,
        ITenantStripeContextLookup lookup,
        IPaymentIntentRepository repo) NewHandler(bool AllowPlatformFallback = false)
    {
        var gateway = Substitute.For<IStripeGateway>();
        var lookup = Substitute.For<ITenantStripeContextLookup>();
        var repo = Substitute.For<IPaymentIntentRepository>();
        repo.GetByBookingIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((PaymentIntent?)null);
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(0));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payment:AllowPlatformFallback"] = AllowPlatformFallback ? "true" : "false",
        }).Build();
        var handler = new CreatePaymentIntentForBookingHandler(
            gateway, lookup, repo, uow, config,
            NullLogger<CreatePaymentIntentForBookingHandler>.Instance);
        return (handler, gateway, lookup, repo);
    }
}
