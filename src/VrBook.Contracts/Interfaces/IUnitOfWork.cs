namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Transaction boundary spanning EF Core <c>SaveChangesAsync</c> + outbox flush.
/// Pipeline behavior <c>TransactionBehavior</c> opens UoW per request when annotated.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken ct = default);
}
