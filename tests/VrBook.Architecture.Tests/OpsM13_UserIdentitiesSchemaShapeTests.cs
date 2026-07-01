using System.Reflection;
using FluentAssertions;
using VrBook.Modules.Identity.Domain;
using VrBook.Modules.Identity.Infrastructure.Persistence;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.13 (M.13.2) — locks in the shape of the new
/// <see cref="UserIdentity"/> aggregate + <see cref="MigrationAuditEntry"/>
/// entity + their DbContext + configuration wiring.
///
/// <para>Design ref: <c>docs/OPS_M_13_IDENTITY_REDESIGN_PLAN.md</c> §2.1.</para>
///
/// <para>These facts DO NOT assert the schema migration itself was
/// applied — that would require a live Postgres. They DO assert that the
/// EF model surface + DbContext have the expected shape, which is what
/// M.13.3's provisioning-handler rewrite depends on.</para>
///
/// <para>Sibling arch tests locking the handler-shape land with M.13.3
/// (they can't be written before the handler exists).</para>
/// </summary>
public sealed class OpsM13_UserIdentitiesSchemaShapeTests
{
    [Fact]
    public void UserIdentity_aggregate_exists_with_required_columns()
    {
        var t = typeof(UserIdentity);
        t.Should().NotBeNull();
        t.GetProperty(nameof(UserIdentity.UserId))?.PropertyType.Should().Be(typeof(Guid),
            because: "user_id is the FK to identity.users.Id per §2.1.");
        t.GetProperty(nameof(UserIdentity.Provider))?.PropertyType.Should().Be(typeof(string),
            because: "provider is the enum discriminator; CHECK constraint at DB level.");
        t.GetProperty(nameof(UserIdentity.ExternalId))?.PropertyType.Should().Be(typeof(string),
            because: "external_id is the provider's user id (oid for Entra; sub for social).");
    }

    [Fact]
    public void UserIdentity_has_Create_factory_and_UpdateLastSeen_method()
    {
        var t = typeof(UserIdentity);
        t.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)
            .Should().NotBeNull(because: "the provisioning handler calls UserIdentity.Create for Branches 2 + 3 per §3.5.");
        t.GetMethod(nameof(UserIdentity.UpdateLastSeen), BindingFlags.Public | BindingFlags.Instance)
            .Should().NotBeNull(because: "Branch 1 of the provisioning handler bumps LastSeenAt.");
    }

    [Fact]
    public void UserIdentity_ctor_is_private_for_EF_only()
    {
        typeof(UserIdentity)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Should().ContainSingle(c => c.IsPrivate && c.GetParameters().Length == 0,
                because: "aggregates hydrate through a private parameterless ctor for EF Core.");
    }

    [Fact]
    public void MigrationAuditEntry_type_exists_as_immutable_read_side()
    {
        var t = typeof(MigrationAuditEntry);
        t.Should().NotBeNull(because: "F11.7's opaque data-heal failures motivated a canonical audit table; see §4.5 of the architectural review.");
        // Immutable from app-side perspective: no public setter is exposed.
        foreach (var prop in t.GetProperties())
        {
            var publicSetter = prop.GetSetMethod(nonPublic: false);
            publicSetter.Should().BeNull(
                because: $"MigrationAuditEntry is read-only from the app; property {prop.Name} must not expose a public setter (INSERTs are raw SQL from migrations).");
        }
    }

    [Fact]
    public void IdentityDbContext_exposes_UserIdentities_and_MigrationAudit_DbSets()
    {
        var t = typeof(IdentityDbContext);
        t.GetProperty("UserIdentities")?.PropertyType
            .Should().Be(typeof(Microsoft.EntityFrameworkCore.DbSet<UserIdentity>),
                because: "the provisioning handler queries UserIdentities on Branch 1.");
        t.GetProperty("MigrationAudit")?.PropertyType
            .Should().Be(typeof(Microsoft.EntityFrameworkCore.DbSet<MigrationAuditEntry>),
                because: "the admin migration-audit endpoint reads through this DbSet.");
    }

    [Fact]
    public void UserIdentityConfiguration_registers_via_assembly_scan()
    {
        // IdentityDbContext calls ApplyConfigurationsFromAssembly, so any
        // IEntityTypeConfiguration<T> in the assembly is picked up. Assert
        // the two new configurations exist so a rename or delete is caught
        // before it silently strips the mapping.
        var asm = typeof(IdentityDbContext).Assembly;
        asm.GetType("VrBook.Modules.Identity.Infrastructure.Persistence.UserIdentityConfiguration")
            .Should().NotBeNull();
        asm.GetType("VrBook.Modules.Identity.Infrastructure.Persistence.MigrationAuditEntryConfiguration")
            .Should().NotBeNull();
    }
}
