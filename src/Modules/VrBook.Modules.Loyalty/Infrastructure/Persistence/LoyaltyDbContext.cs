using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure.Persistence;
using VrBook.Modules.Loyalty.Domain;

namespace VrBook.Modules.Loyalty.Infrastructure.Persistence;

public sealed class LoyaltyDbContext(
    DbContextOptions<LoyaltyDbContext> options,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : BaseDbContext(options, currentUser, clock)
{
    public const string SchemaName = "loyalty";
    protected override string Schema => SchemaName;

    public DbSet<LoyaltyAccount> Accounts => Set<LoyaltyAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LoyaltyDbContext).Assembly);
    }
}

internal sealed class LoyaltyAccountConfiguration : IEntityTypeConfiguration<LoyaltyAccount>
{
    public void Configure(EntityTypeBuilder<LoyaltyAccount> builder)
    {
        builder.ToTable("accounts", LoyaltyDbContext.SchemaName);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.HasIndex(x => x.UserId).IsUnique();
        builder.Property(x => x.Tier).HasColumnName("tier").HasConversion<int>().IsRequired();
        builder.Property(x => x.CompletedStayCount).HasColumnName("completed_stay_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.LastEvaluatedAt).HasColumnName("last_evaluated_at");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        builder.Property(x => x.RowVersion).HasColumnName("row_version");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.Property(x => x.DeletedBy).HasColumnName("deleted_by");
    }
}
