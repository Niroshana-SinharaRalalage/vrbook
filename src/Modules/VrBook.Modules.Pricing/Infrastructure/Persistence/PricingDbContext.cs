using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure.Persistence;
using VrBook.Modules.Pricing.Domain;

namespace VrBook.Modules.Pricing.Infrastructure.Persistence;

public sealed class PricingDbContext(
    DbContextOptions<PricingDbContext> options,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : BaseDbContext(options, currentUser, clock)
{
    public const string SchemaName = "pricing";
    protected override string Schema => SchemaName;

    public DbSet<PricingPlan> PricingPlans => Set<PricingPlan>();
    public DbSet<Fee> Fees => Set<Fee>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PricingDbContext).Assembly);
    }
}
