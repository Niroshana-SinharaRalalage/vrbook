using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VrBook.Modules.Booking.Domain;

namespace VrBook.Modules.Booking.Infrastructure.Persistence;

internal sealed class AvailabilityBlockConfiguration : IEntityTypeConfiguration<AvailabilityBlock>
{
    public void Configure(EntityTypeBuilder<AvailabilityBlock> b)
    {
        b.ToTable("availability_blocks", BookingDbContext.SchemaName);
        b.HasKey(x => x.Id);

        b.Property(x => x.PropertyId).HasColumnName("property_id").IsRequired();
        b.HasIndex(x => x.PropertyId);

        // Forward-compat per REPLAN.md §10.1. FK to identity.tenants(id) is declared
        // at the SQL level in the migration (cross-schema, no EF nav property).
        b.Property(x => x.TenantId).HasColumnName("tenant_id");

        b.Property(x => x.StartDate).HasColumnName("start_date").HasColumnType("date").IsRequired();
        b.Property(x => x.EndDate).HasColumnName("end_date").HasColumnType("date").IsRequired();
        b.HasIndex(x => new { x.PropertyId, x.StartDate, x.EndDate })
            .HasDatabaseName("ix_availability_blocks_property_dates");

        b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(200);

        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.CreatedBy).HasColumnName("created_by");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        b.Property(x => x.RowVersion).HasColumnName("row_version");
        b.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        b.Property(x => x.DeletedBy).HasColumnName("deleted_by");
    }
}
