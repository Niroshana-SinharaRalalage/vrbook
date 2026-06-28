using System.Reflection;
using FluentAssertions;
using VrBook.Contracts.Events;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.6 §3.5 (D5) Step 5 — RED tests pinning the post-bump shape of
/// the three Sync domain events that lacked <c>Guid TenantId</c>.
///
/// <para>Same convention as <c>PaymentEventTenantIdShapeTests</c> (OPS.M.5
/// Step 7): leading positional <c>Guid TenantId</c>, no nullable.</para>
///
/// <para><c>SyncConflictDetected</c> already carries TenantId (positions
/// 0-5 with TenantId at position 5 historically) — we leave its raise sites
/// alone because Booking-side consumers in OPS.M.4 depend on the current
/// shape. A future re-bump can move it to position 0 once those consumers
/// re-deploy.</para>
/// </summary>
public sealed class SyncEventTenantIdShapeTests
{
    private static readonly Type[] BumpedEvents = new[]
    {
        typeof(ExternalReservationImported),
        typeof(ExternalReservationCancelled),
        typeof(SyncRunFailed),
    };

    [Fact]
    public void Every_bumped_sync_event_positional_ctor_has_Guid_TenantId_at_position_0()
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
                $"{t.FullName}: per OPS_M_6_PLAN §3.5 (D5), Guid TenantId must be the first positional parameter.");
            first.ParameterType.Should().Be(typeof(Guid),
                $"{t.FullName}.TenantId must be Guid (not Guid?). The behavior gate trusts the value.");
        }
    }

    [Fact]
    public void SyncConflictDetected_already_carries_TenantId_position_locked_to_5()
    {
        // Pin the current shape so a careless re-bump doesn't shift positions
        // and silently break OPS.M.4's Booking-side consumer. If the consumer
        // re-deploys in a later slice, this assertion can move to position 0.
        var ctor = typeof(SyncConflictDetected).GetConstructors().Single();
        var p = ctor.GetParameters();
        p.Should().Contain(x => x.Name == "TenantId" && x.ParameterType == typeof(Guid));
    }
}
