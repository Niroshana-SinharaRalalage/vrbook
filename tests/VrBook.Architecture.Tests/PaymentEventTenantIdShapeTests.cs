using System.Reflection;
using FluentAssertions;
using VrBook.Contracts.Events;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.5 Step 7 — RED tests pinning the post-bump shape of Payment
/// domain events per `docs/OPS_M_5_PLAN.md` §3.9 (D9) + §9 Step 7.
///
/// <para>The plan: every Payment event whose downstream consumers need tenant
/// scope gets <c>Guid TenantId</c> as the leading positional parameter on the
/// record — same shape OPS.M.4 Step 1 used for the 13 booking-and-friends
/// events. <c>DisputeOpened</c> is intentionally NOT bumped (no Phase 1.5
/// consumer needs it; auto-respond is Phase 2 per MTOP §11).</para>
///
/// <para>The check at "position 0" enforces the convention so downstream
/// consumers can deserialize the event with positional matching without
/// branching on per-event positions.</para>
/// </summary>
public sealed class PaymentEventTenantIdShapeTests
{
    private static readonly Type[] BumpedEvents = new[]
    {
        typeof(PaymentAuthorized),
        typeof(PaymentCaptured),
        typeof(PaymentFailed),
        typeof(RefundIssued),
    };

    [Fact]
    public void Every_payment_event_positional_ctor_has_Guid_TenantId_at_position_0()
    {
        foreach (var t in BumpedEvents)
        {
            var ctor = t.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .OrderByDescending(c => c.GetParameters().Length)
                .First();
            var parameters = ctor.GetParameters();

            parameters.Should().NotBeEmpty($"{t.FullName} should have positional ctor params.");
            var first = parameters[0];
            first.Name.Should().Be("TenantId",
                $"{t.FullName}: per OPS_M_5_PLAN §3.9 (D9), Guid TenantId must be the first positional parameter.");
            first.ParameterType.Should().Be(typeof(Guid),
                $"{t.FullName}.TenantId must be Guid (not Guid?). The behavior gate trusts the value.");
        }
    }

    [Fact]
    public void DisputeOpened_is_intentionally_NOT_bumped_with_TenantId()
    {
        // Locks the §3.9 carve-out so a future PR doesn't silently broaden the
        // bump set. If you need DisputeOpened with TenantId, raise it in the
        // owning slice's plan (auto-respond is Phase 2 per MTOP §11) and
        // remove this assertion.
        var ctor = typeof(DisputeOpened).GetConstructors().Single();
        ctor.GetParameters().Should().NotContain(p => p.Name == "TenantId",
            "DisputeOpened is deferred per OPS_M_5_PLAN §3.9 (D9) — Phase 2 auto-respond owns it.");
    }
}
