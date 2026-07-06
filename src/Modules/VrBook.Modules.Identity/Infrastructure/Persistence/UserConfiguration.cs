using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VrBook.Modules.Identity.Domain;

namespace VrBook.Modules.Identity.Infrastructure.Persistence;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users", IdentityDbContext.SchemaName);
        b.HasKey(u => u.Id);

        b.Property(u => u.Email)
            .HasColumnName("email")
            .HasMaxLength(320)
            .HasConversion(v => v.Value, v => new Email(v))
            .IsRequired();
        // Slice OPS.M.13.2 shipped the partial-UNIQUE `users_email_active_lower_uq`
        // on `lower(email) WHERE deleted_at IS NULL`. Declare it here so the EF
        // model snapshot reflects the DB state — the actual index creation lives
        // in the migration (EF Fluent API cannot emit partial or expression
        // indexes natively).
        b.HasIndex(u => u.Email)
            .HasDatabaseName("users_email_active_lower_uq")
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        b.Property(u => u.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();

        b.Property(u => u.Phone)
            .HasColumnName("phone")
            .HasMaxLength(40)
            .HasConversion(v => v.Value, v => new PhoneNumber(v));

        // Slice OPS.M.21 (M.15 follow-up A step 2) — IsOwner/IsAdmin
        // properties dropped from the User aggregate; the M.21.A.3 migration
        // drops the DB columns and regenerates the model snapshot.
        // OPS.M.8 §3.1 (D1) — partial index "WHERE is_platform_admin = true"
        // keeps the index tiny because the population is small (low single
        // digits across the whole platform). Lookup speed at startup matters
        // because the value flows into every authenticated request.
        b.Property(u => u.IsPlatformAdmin).HasColumnName("is_platform_admin").HasDefaultValue(false);
        b.HasIndex(u => u.IsPlatformAdmin)
            .HasDatabaseName("ix_users_is_platform_admin")
            .HasFilter("\"is_platform_admin\" = true");
        b.Property(u => u.EmailVerified).HasColumnName("email_verified").HasDefaultValue(false);
        b.Property(u => u.LastLoginAt).HasColumnName("last_login_at");

        // Audit + soft-delete columns inherited from AggregateRoot
        b.Property(u => u.CreatedAt).HasColumnName("created_at");
        b.Property(u => u.CreatedBy).HasColumnName("created_by");
        b.Property(u => u.UpdatedAt).HasColumnName("updated_at");
        b.Property(u => u.UpdatedBy).HasColumnName("updated_by");
        b.Property(u => u.RowVersion).HasColumnName("row_version");
        b.Property(u => u.DeletedAt).HasColumnName("deleted_at");
        b.Property(u => u.DeletedBy).HasColumnName("deleted_by");
    }
}
