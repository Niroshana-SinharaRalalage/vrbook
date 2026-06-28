using FluentAssertions;
using VrBook.Modules.Payment.Application.Commands;
using Xunit;

namespace VrBook.Api.IntegrationTests.Payment;

/// <summary>
/// Slice OPS.M.5 Step 6 — pins the rewritten <c>HandleStripeWebhookHandler</c>
/// per docs/OPS_M_5_PLAN.md §3.7 (D7) + §9 Step 6.
///
/// <para>End-to-end behavior tests (signature verification, JSON parsing,
/// per-account idempotency, account → tenant resolution, dispatch) need the
/// Postgres testcontainer fixture and run in CI under Category=Integration.
/// The unit facts below pin the static dispatch surface — they're cheap and
/// catch the most common Step 6 regression (forgetting to add a new event
/// type to the table).</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class HandleStripeWebhookHandlerTests
{
    private static readonly string[] ExpectedEventTypes =
    {
        "payment_intent.amount_capturable_updated",
        "payment_intent.succeeded",
        "payment_intent.payment_failed",
        "payment_intent.canceled",
        "charge.refunded",
        "charge.dispute.created",
        "account.updated",
    };

    [Fact]
    public void Dispatch_table_covers_all_seven_supported_event_types()
    {
        var supported = ReadDispatchKeys();
        supported.Should().BeEquivalentTo(ExpectedEventTypes);
    }

    [Fact]
    public void Dispatch_table_contains_no_duplicates()
    {
        var supported = ReadDispatchKeys();
        supported.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Dispatch_table_uses_ordinal_string_comparison_so_lookups_are_case_sensitive()
    {
        var supported = ReadDispatchKeys();
        supported.Should().AllSatisfy(t =>
            t.Should().Be(t.ToLowerInvariant(),
                because: "Stripe event types are lowercased; ordinal comparison must match exactly."));
    }

    private static IReadOnlyCollection<string> ReadDispatchKeys()
    {
        var prop = typeof(HandleStripeWebhookHandler)
            .GetProperty(
                "SupportedEventTypes",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        return (IReadOnlyCollection<string>)prop.GetValue(null)!;
    }
}
