using VrBook.Api.IntegrationTests.Auth;

namespace VrBook.Api.IntegrationTests.Multitenancy;

/// <summary>
/// Slice OPS.M.14.1 — persona registry for the two-tenant fixture. Holds
/// the stable OIDs the fixture's seed data + downstream tests both reference.
///
/// <para>Type name retained from the pre-M.14 <c>TwoTenantDevAuthHandler</c>
/// so ~25 call sites across <c>CarveOutAppLayerTests</c>,
/// <c>CrossTenantEndpointMatrix</c>, <c>PlatformAdminBypassFactPack</c>,
/// <c>CrossTenantRejectionAuditFactPack</c>,
/// <c>PlatformAdminPromoteRevokeSmokeTest</c>, <c>JwtSmokeTests</c>,
/// <c>TwoTenantApiFixtureTests</c>, and the fixture itself keep the same
/// <c>TwoTenantTestAuthHandler.OwnerAOid</c>-style references. The class no
/// longer inherits from <c>AuthenticationHandler</c> — auth is handled by
/// the generic <see cref="TestAuthHandler"/> registered by the fixture.</para>
/// </summary>
public static class TwoTenantTestAuthHandler
{
    /// <summary>OID assigned to the seeded TenantA owner. Stable across runs.</summary>
    public const string OwnerAOid = "test-owner-tenant-a";

    /// <summary>OID assigned to the seeded TenantB owner. Stable across runs.</summary>
    public const string OwnerBOid = "test-owner-tenant-b";

    /// <summary>OID assigned to the seeded PlatformAdmin user.</summary>
    public const string PlatformAdminOid = "test-platform-admin";

    /// <summary>
    /// The persona lookup <see cref="TestAuthHandler"/> consumes. Registered
    /// once at fixture construction; keyed by the <c>X-Test-Persona</c>
    /// header value each test client sets via <c>CreateClientAs(persona)</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, TestPersona> Personas { get; } =
        new Dictionary<string, TestPersona>
        {
            ["OwnerA"] = new(OwnerAOid, "owner-a@vrbook.test", "Owner A", IsOwner: true, IsAdmin: true),
            ["OwnerB"] = new(OwnerBOid, "owner-b@vrbook.test", "Owner B", IsOwner: true, IsAdmin: true),
            ["PlatformAdmin"] = new(PlatformAdminOid, "platform-admin@vrbook.test", "Platform Admin", IsOwner: false, IsAdmin: false),
        };
}
