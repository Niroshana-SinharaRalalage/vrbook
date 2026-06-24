using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VrBook.Modules.Pricing.Domain;

namespace VrBook.Modules.Pricing.Infrastructure.Persistence;

internal sealed class PricingPlanConfiguration : IEntityTypeConfiguration<PricingPlan>
{
    public void Configure(EntityTypeBuilder<PricingPlan> b)
    {
        b.ToTable("pricing_plans", PricingDbContext.SchemaName);
        b.HasKey(p => p.Id);

        b.Property(p => p.PropertyId).HasColumnName("property_id").IsRequired();
        b.HasIndex(p => p.PropertyId).IsUnique();

        b.Property(p => p.BaseNightlyRate).HasColumnName("base_nightly_rate").HasColumnType("numeric(12,2)").IsRequired();
        b.Property(p => p.WeekendRate).HasColumnName("weekend_rate").HasColumnType("numeric(12,2)").IsRequired();
        b.Property(p => p.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        b.Property(p => p.MinStayNights).HasColumnName("min_stay_nights").HasDefaultValue(1);
        b.Property(p => p.MaxStayNights).HasColumnName("max_stay_nights").HasDefaultValue(30);
        b.Property(p => p.DynamicEnabled).HasColumnName("dynamic_enabled").HasDefaultValue(false);

        b.Property(p => p.CreatedAt).HasColumnName("created_at");
        b.Property(p => p.CreatedBy).HasColumnName("created_by");
        b.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        b.Property(p => p.UpdatedBy).HasColumnName("updated_by");
        // row_version is intentionally NOT configured as a concurrency token -
        // see BaseDbContext for the Phase-1 rationale.
        b.Property(p => p.RowVersion).HasColumnName("row_version");
        b.Property(p => p.DeletedAt).HasColumnName("deleted_at");
        b.Property(p => p.DeletedBy).HasColumnName("deleted_by");

        b.HasMany(p => p.Fees)
            .WithOne()
            .HasForeignKey(f => f.PricingPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(p => p.Rules)
            .WithOne()
            .HasForeignKey(r => r.PricingPlanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
