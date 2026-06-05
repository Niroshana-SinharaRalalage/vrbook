using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VrBook.Modules.Catalog.Domain;

namespace VrBook.Modules.Catalog.Infrastructure.Persistence;

internal sealed class HouseRuleConfiguration : IEntityTypeConfiguration<HouseRule>
{
    public void Configure(EntityTypeBuilder<HouseRule> b)
    {
        b.ToTable("house_rules", CatalogDbContext.SchemaName);
        b.HasKey(h => h.Id);
        b.Property(h => h.PropertyId).HasColumnName("property_id").IsRequired();
        b.Property(h => h.RuleText).HasColumnName("rule_text").HasColumnType("text").IsRequired();
        b.Property(h => h.SortOrder).HasColumnName("sort_order");
        b.HasIndex(h => new { h.PropertyId, h.SortOrder });
    }
}
