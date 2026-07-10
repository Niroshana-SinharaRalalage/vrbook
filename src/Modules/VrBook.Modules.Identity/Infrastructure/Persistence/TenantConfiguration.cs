using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VrBook.Modules.Identity.Domain;

namespace VrBook.Modules.Identity.Infrastructure.Persistence;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.ToTable("tenants", IdentityDbContext.SchemaName, t =>
        {
            // Lifecycle invariant: app code enforces this too (Tenant.Activate/Suspend/etc),
            // but the CHECK is defence-in-depth in case someone seeds a row directly via SQL.
            // `Deleted` is represented by DeletedAt non-null, not a status value.
            t.HasCheckConstraint(
                "ck_tenants_status",
                "status IN ('PendingOnboarding','Active','Suspended','Closed')");
        });
        b.HasKey(t => t.Id);

        b.Property(t => t.Slug).HasColumnName("slug").HasMaxLength(64).IsRequired();
        b.HasIndex(t => t.Slug).IsUnique();

        b.Property(t => t.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();

        // OPS.M.1 — full Tenant shape per docs/OPS_M_1_PLAN.md §3.1.
        b.Property(t => t.Status)
            .HasColumnName("status")
            .HasMaxLength(32)
            .HasDefaultValue(Tenant.StatusPendingOnboarding)
            .IsRequired();

        b.Property(t => t.DefaultCurrency)
            .HasColumnName("default_currency")
            .HasColumnType("char(3)")
            .HasDefaultValue("USD")
            .IsRequired();

        b.Property(t => t.DefaultTimezone)
            .HasColumnName("default_timezone")
            .HasMaxLength(64)
            .HasDefaultValue("UTC")
            .IsRequired();

        b.Property(t => t.SupportEmail)
            .HasColumnName("support_email")
            .HasMaxLength(320)
            .HasConversion(v => v.Value, v => new Email(v))
            .HasDefaultValue(new Email("support@vrbook.example.com"))
            .IsRequired();

        b.Property(t => t.PlatformFeeBps)
            .HasColumnName("platform_fee_bps")
            .HasDefaultValue(1500)
            .IsRequired();

        b.Property(t => t.StripeAccountId)
            .HasColumnName("stripe_account_id")
            .HasMaxLength(64);

        b.Property(t => t.StripeAccountStatus)
            .HasColumnName("stripe_account_status")
            .HasMaxLength(64);

        // OPS.M.5 §3.8 (D8) — readiness flags driven by Stripe's account.updated webhook.
        b.Property(t => t.ChargesEnabled)
            .HasColumnName("charges_enabled")
            .HasDefaultValue(false)
            .IsRequired();
        b.Property(t => t.PayoutsEnabled)
            .HasColumnName("payouts_enabled")
            .HasDefaultValue(false)
            .IsRequired();

        b.Property(t => t.SuspendedReason)
            .HasColumnName("suspended_reason")
            .HasMaxLength(500);

        // Slice OPS.2.2 — nullable shadow property: NULL for real tenants; the
        // migrator's SeedE2eBackfill sets TRUE (via raw SQL) on the isolated
        // e2e-tenant only. Mapped as a shadow property because no domain
        // behaviour reads or writes it — the column is purely a test-data
        // marker owned by the SQL seed. Declared here so the EF model snapshot
        // tracks it and future migrations don't attempt to drop it.
        // See docs/OPS_2_PLAYWRIGHT_PLAN.md §6.
        b.Property<bool?>("is_e2e").HasColumnName("is_e2e");

        b.Property(t => t.CreatedAt).HasColumnName("created_at");
        b.Property(t => t.CreatedBy).HasColumnName("created_by");
        b.Property(t => t.UpdatedAt).HasColumnName("updated_at");
        b.Property(t => t.UpdatedBy).HasColumnName("updated_by");
        b.Property(t => t.RowVersion).HasColumnName("row_version");
        b.Property(t => t.DeletedAt).HasColumnName("deleted_at");
        b.Property(t => t.DeletedBy).HasColumnName("deleted_by");
    }
}
