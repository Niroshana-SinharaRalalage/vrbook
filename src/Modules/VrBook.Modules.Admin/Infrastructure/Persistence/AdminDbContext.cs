using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure.Persistence;
using VrBook.Modules.Admin.Domain;

namespace VrBook.Modules.Admin.Infrastructure.Persistence;

/// <summary>
/// VRB-203 — the Admin bounded context's DbContext. Owns the <c>admin</c> schema and
/// the global <c>feature_flags</c> override table. Registered as a <b>plain</b>
/// (non-tenant-scoped) DbContext — feature flags are platform-global, so the
/// tenant-GUC RLS interceptor is deliberately NOT attached.
/// </summary>
public sealed class AdminDbContext(
    DbContextOptions<AdminDbContext> options,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : BaseDbContext(options, currentUser, clock)
{
    public const string SchemaName = "admin";
    protected override string Schema => SchemaName;

    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();

    // VRB-216 — product-settings tables (platform-global, no RLS). The per-tenant
    // platform fee is NOT here: it stays the single source of truth on
    // identity.tenants.PlatformFeeBps (set via SetTenantPlatformFeeBpsCommand).
    public DbSet<CancellationTiers> CancellationTiers => Set<CancellationTiers>();
    public DbSet<TaxPostureRow> TaxPosture => Set<TaxPostureRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AdminDbContext).Assembly);
    }
}

internal sealed class CancellationTiersConfiguration : IEntityTypeConfiguration<CancellationTiers>
{
    public void Configure(EntityTypeBuilder<CancellationTiers> builder)
    {
        builder.ToTable("cancellation_tiers", AdminDbContext.SchemaName);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Version).HasColumnName("version").IsRequired();
        builder.Property(x => x.FirstTierDays).HasColumnName("first_tier_days").IsRequired();
        builder.Property(x => x.SecondTierDays).HasColumnName("second_tier_days").IsRequired();
        builder.Property(x => x.MiddleTierRefundPct).HasColumnName("middle_tier_pct").IsRequired();
        builder.Property(x => x.FinalCutoffHours).HasColumnName("final_cutoff_hours").IsRequired();
        builder.Property(x => x.UpgradePricePct).HasColumnName("upgrade_price_pct").IsRequired();
        builder.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
    }
}

internal sealed class TaxPostureConfiguration : IEntityTypeConfiguration<TaxPostureRow>
{
    public void Configure(EntityTypeBuilder<TaxPostureRow> builder)
    {
        builder.ToTable("tax_posture", AdminDbContext.SchemaName);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.FacilitatorActive).HasColumnName("facilitator_active").IsRequired();
        builder.Property(x => x.PerStateJson).HasColumnName("per_state_json").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
    }
}

internal sealed class FeatureFlagConfiguration : IEntityTypeConfiguration<FeatureFlag>
{
    public void Configure(EntityTypeBuilder<FeatureFlag> builder)
    {
        builder.ToTable("feature_flags", AdminDbContext.SchemaName);
        builder.HasKey(x => x.Key);
        builder.Property(x => x.Key).HasColumnName("key").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Enabled).HasColumnName("enabled").IsRequired();
        builder.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
    }
}
