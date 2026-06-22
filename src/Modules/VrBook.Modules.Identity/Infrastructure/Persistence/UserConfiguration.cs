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

        b.Property(u => u.B2CObjectId).HasColumnName("b2c_object_id").HasMaxLength(64).IsRequired();
        b.HasIndex(u => u.B2CObjectId).IsUnique();

        b.Property(u => u.Email)
            .HasColumnName("email")
            .HasMaxLength(320)
            .HasConversion(v => v.Value, v => new Email(v))
            .IsRequired();
        // Slice 4 polish: relaxed to a non-unique index. DevAuth personas can
        // share an inbox (e.g. niroshanaks@gmail.com) for end-to-end staging
        // verification. Production uniqueness is enforced upstream at the
        // Entra IdP (ADR-0012), so the DB constraint was belt-and-suspenders.
        b.HasIndex(u => u.Email);

        b.Property(u => u.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();

        b.Property(u => u.Phone)
            .HasColumnName("phone")
            .HasMaxLength(40)
            .HasConversion(v => v.Value, v => new PhoneNumber(v));

        b.Property(u => u.IsOwner).HasColumnName("is_owner").HasDefaultValue(false);
        b.Property(u => u.IsAdmin).HasColumnName("is_admin").HasDefaultValue(false);
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
