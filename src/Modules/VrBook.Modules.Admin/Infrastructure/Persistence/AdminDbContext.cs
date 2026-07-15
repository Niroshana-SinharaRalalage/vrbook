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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AdminDbContext).Assembly);
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
