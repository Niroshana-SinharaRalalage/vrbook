using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace VrBook.Infrastructure.Persistence;

/// <summary>
/// Slice OPS.M.9 §4.4 (D4) — default implementation of
/// <see cref="IRlsBypassDbContextFactory{TContext}"/>. One DI registration
/// per module's DbContext (each module's <c>Add&lt;Module&gt;Module</c>
/// extension wires it).
///
/// <para>The implementation is non-abstract and generic — no per-module
/// sealed subclass needed, because the wrapping pattern uses a
/// separately-disposable <see cref="BypassedDbContext{TContext}"/> instead
/// of subclassing the DbContext.</para>
/// </summary>
public sealed class RlsBypassDbContextFactory<TContext>(
    IDbContextFactory<TContext> inner,
    ILogger<RlsBypassDbContextFactory<TContext>> logger)
    : IRlsBypassDbContextFactory<TContext>
    where TContext : DbContext
{
    public async Task<BypassedDbContext<TContext>> CreateForBypassAsync(
        string reason, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        logger.LogInformation(
            "RLS bypass open db_context={DbContext} reason={Reason}",
            typeof(TContext).Name, reason);

        IDisposable? scope = null;
        try
        {
            scope = RlsBypassScope.Enter();
            var ctx = await inner.CreateDbContextAsync(ct);
            return new BypassedDbContext<TContext>(ctx, scope);
        }
        catch
        {
            scope?.Dispose();
            throw;
        }
    }
}
