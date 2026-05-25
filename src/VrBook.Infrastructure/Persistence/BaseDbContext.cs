using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;

namespace VrBook.Infrastructure.Persistence;

/// <summary>
/// Base for every per-context DbContext. Provides:
/// <list type="bullet">
///   <item>Schema selection — concrete contexts override <see cref="Schema"/>.</item>
///   <item>Audit columns auto-populated in <see cref="SaveChangesAsync"/>.</item>
///   <item>Soft-delete global query filter for <see cref="AggregateRoot"/>-derived entities.</item>
///   <item>Optimistic concurrency token mapped to <c>row_version</c>.</item>
/// </list>
/// </summary>
public abstract class BaseDbContext(
    DbContextOptions options,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : DbContext(options), IUnitOfWork
{
    /// <summary>The Postgres schema this context owns (e.g., "catalog", "booking").</summary>
    protected abstract string Schema { get; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clr = entityType.ClrType;
            if (typeof(AggregateRoot).IsAssignableFrom(clr))
            {
                // Concurrency token
                modelBuilder.Entity(clr)
                    .Property(nameof(AggregateRoot.RowVersion))
                    .IsConcurrencyToken();

                // Soft delete filter — `e => e.DeletedAt == null`
                var parameter = System.Linq.Expressions.Expression.Parameter(clr, "e");
                var deletedAt = System.Linq.Expressions.Expression.Property(
                    parameter, nameof(AggregateRoot.DeletedAt));
                var nullConst = System.Linq.Expressions.Expression.Constant(null, typeof(DateTimeOffset?));
                var body = System.Linq.Expressions.Expression.Equal(deletedAt, nullConst);
                var lambda = System.Linq.Expressions.Expression.Lambda(body, parameter);

                modelBuilder.Entity(clr).HasQueryFilter(lambda);
            }
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAudit();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Open a database transaction; ambient to <see cref="SaveChangesAsync"/>.</summary>
    public async Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken ct = default)
    {
        var tx = await Database.BeginTransactionAsync(ct);
        return new TransactionScope(tx);
    }

    private void ApplyAudit()
    {
        var now = clock.UtcNow;
        var actor = currentUser.UserId;

        foreach (var entry in ChangeTracker.Entries<AggregateRoot>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(nameof(AggregateRoot.CreatedAt)).CurrentValue = now;
                    entry.Property(nameof(AggregateRoot.CreatedBy)).CurrentValue = actor;
                    entry.Property(nameof(AggregateRoot.UpdatedAt)).CurrentValue = now;
                    entry.Property(nameof(AggregateRoot.UpdatedBy)).CurrentValue = actor;
                    break;
                case EntityState.Modified:
                    entry.Property(nameof(AggregateRoot.UpdatedAt)).CurrentValue = now;
                    entry.Property(nameof(AggregateRoot.UpdatedBy)).CurrentValue = actor;
                    entry.Property(nameof(AggregateRoot.RowVersion)).CurrentValue =
                        (long)entry.Property(nameof(AggregateRoot.RowVersion)).CurrentValue! + 1;
                    break;
                case EntityState.Deleted:
                    // Convert hard delete to soft delete.
                    entry.State = EntityState.Modified;
                    entry.Property(nameof(AggregateRoot.DeletedAt)).CurrentValue = now;
                    entry.Property(nameof(AggregateRoot.DeletedBy)).CurrentValue = actor;
                    break;
            }
        }
    }

    private sealed class TransactionScope(IDbContextTransaction tx) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await tx.CommitAsync();
            await tx.DisposeAsync();
        }
    }
}
