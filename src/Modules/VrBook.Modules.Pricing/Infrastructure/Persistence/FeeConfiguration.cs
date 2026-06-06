using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VrBook.Modules.Pricing.Domain;

namespace VrBook.Modules.Pricing.Infrastructure.Persistence;

internal sealed class FeeConfiguration : IEntityTypeConfiguration<Fee>
{
    public void Configure(EntityTypeBuilder<Fee> b)
    {
        b.ToTable("fees", PricingDbContext.SchemaName);
        b.HasKey(f => f.Id);
        b.Property(f => f.PricingPlanId).HasColumnName("pricing_plan_id").IsRequired();
        b.Property(f => f.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(40).IsRequired();
        b.Property(f => f.Amount).HasColumnName("amount").HasColumnType("numeric(12,2)").IsRequired();
        b.Property(f => f.Basis).HasColumnName("basis").HasConversion<string>().HasMaxLength(40).IsRequired();
        b.Property(f => f.FreeThreshold).HasColumnName("free_threshold");
        b.Property(f => f.Label).HasColumnName("label").HasMaxLength(120).IsRequired();
        b.HasIndex(f => f.PricingPlanId);
    }
}
