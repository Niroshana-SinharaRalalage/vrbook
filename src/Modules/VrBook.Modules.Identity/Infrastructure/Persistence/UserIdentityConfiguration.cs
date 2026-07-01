using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VrBook.Modules.Identity.Domain;

namespace VrBook.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Slice OPS.M.13 — EF configuration for <see cref="UserIdentity"/>. Maps
/// to <c>identity.user_identities</c> per
/// <c>docs/OPS_M_13_IDENTITY_REDESIGN_PLAN.md</c> §2.1.
/// </summary>
internal sealed class UserIdentityConfiguration : IEntityTypeConfiguration<UserIdentity>
{
    public void Configure(EntityTypeBuilder<UserIdentity> b)
    {
        b.ToTable("user_identities", "identity");
        b.HasKey(x => x.Id);

        b.Property(x => x.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        b.Property(x => x.Provider)
            .HasColumnName("provider")
            .HasMaxLength(32)
            .IsRequired();

        b.Property(x => x.ExternalId)
            .HasColumnName("external_id")
            .HasMaxLength(255)
            .IsRequired();

        b.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at").IsRequired();
        b.Property(x => x.LastSeenAt).HasColumnName("last_seen_at").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        b.Property(x => x.DeletedBy).HasColumnName("deleted_by");

        b.Property<long>("RowVersion")
            .HasColumnName("row_version")
            .IsRowVersion();

        // FK to identity.users. ON DELETE CASCADE per the design doc:
        // if a users row is hard-deleted (e.g., GDPR erasure) all its
        // identity links should follow. Soft-delete on the user (the
        // normal path) leaves identities untouched.
        b.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Uniqueness on (provider, external_id) is the invariant that
        // makes the email-first provisioning algorithm's race handling
        // work — two tabs racing on the same fresh oid see one succeed
        // and one 23505; the handler catches and re-queries.
        b.HasIndex(x => new { x.Provider, x.ExternalId })
            .IsUnique()
            .HasDatabaseName("user_identities_provider_extid_uq");

        b.HasIndex(x => x.UserId).HasDatabaseName("ix_user_identities_user_id");

        // Soft-delete filter — inherited from AggregateRoot convention
        // used by User + TenantMembership.
        b.HasQueryFilter(x => x.DeletedAt == null);
    }
}
