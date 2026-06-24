using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VrBook.Modules.Pricing.Domain;

namespace VrBook.Modules.Pricing.Infrastructure.Persistence;

internal sealed class PricingRuleConfiguration : IEntityTypeConfiguration<PricingRule>
{
    public void Configure(EntityTypeBuilder<PricingRule> b)
    {
        b.ToTable("pricing_rules", PricingDbContext.SchemaName);
        b.HasKey(r => r.Id);

        b.Property(r => r.PricingPlanId).HasColumnName("pricing_plan_id").IsRequired();
        b.Property(r => r.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(40).IsRequired();
        b.Property(r => r.Priority).HasColumnName("priority").IsRequired();
        b.Property(r => r.StartDate).HasColumnName("start_date");
        b.Property(r => r.EndDate).HasColumnName("end_date");
        b.Property(r => r.DayOfWeekMask).HasColumnName("day_of_week_mask");
        b.Property(r => r.MinNights).HasColumnName("min_nights");
        b.Property(r => r.MaxNights).HasColumnName("max_nights");
        b.Property(r => r.DaysBeforeCheckin).HasColumnName("days_before_checkin");
        b.Property(r => r.AdjustmentKind).HasColumnName("adjustment_kind").HasConversion<string>().HasMaxLength(40).IsRequired();
        b.Property(r => r.AdjustmentValue).HasColumnName("adjustment_value").HasColumnType("numeric(12,4)").IsRequired();
        b.Property(r => r.IsEnabled).HasColumnName("is_enabled").HasDefaultValue(true).IsRequired();

        b.HasIndex(r => new { r.PricingPlanId, r.Priority });
    }
}
