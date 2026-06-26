using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VrBook.Modules.Identity.Domain;

namespace VrBook.Modules.Identity.Infrastructure.Persistence;

internal sealed class TenantMembershipConfiguration : IEntityTypeConfiguration<TenantMembership>
{
    public void Configure(EntityTypeBuilder<TenantMembership> b)
    {
        b.ToTable("tenant_memberships", IdentityDbContext.SchemaName, t =>
        {
            // Role CHECK: app code (TenantMembership.Create + .ChangeRole) also enforces,
            // but the CHECK belts the floor against direct SQL seeds and any future
            // EF interceptors that might bypass the aggregate factory.
            t.HasCheckConstraint(
                "ck_tenant_memberships_role",
                "role IN ('tenant_admin','tenant_member')");
        });
        b.HasKey(m => m.Id);

        b.Property(m => m.UserId).HasColumnName("user_id").IsRequired();
        b.HasOne<User>()
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Property(m => m.TenantId).HasColumnName("tenant_id").IsRequired();
        b.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(m => m.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Property(m => m.Role).HasColumnName("role").HasMaxLength(32).IsRequired();

        b.Property(m => m.IsPrimary).HasColumnName("is_primary").HasDefaultValue(false);

        // Audit + soft-delete columns from AggregateRoot.
        b.Property(m => m.CreatedAt).HasColumnName("created_at");
        b.Property(m => m.CreatedBy).HasColumnName("created_by");
        b.Property(m => m.UpdatedAt).HasColumnName("updated_at");
        b.Property(m => m.UpdatedBy).HasColumnName("updated_by");
        b.Property(m => m.RowVersion).HasColumnName("row_version");
        b.Property(m => m.DeletedAt).HasColumnName("deleted_at");
        b.Property(m => m.DeletedBy).HasColumnName("deleted_by");

        // Hot-path index for the OPS.M.2 middleware enrichment:
        // db.Set<TenantMembership>().Where(m => m.UserId == userId && m.DeletedAt == null)
        b.HasIndex(m => m.UserId)
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ix_tenant_memberships_user");

        // Partial unique: at most one live membership per (user, tenant) pair.
        // Allows a soft-deleted membership to coexist with a fresh re-add.
        b.HasIndex(m => new { m.UserId, m.TenantId })
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ux_tenant_memberships_user_tenant");
    }
}
