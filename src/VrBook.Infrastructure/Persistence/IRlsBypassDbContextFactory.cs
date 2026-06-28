using Microsoft.EntityFrameworkCore;

namespace VrBook.Infrastructure.Persistence;

/// <summary>
/// Slice OPS.M.9 §4.4 (D4) — opens a fresh DbContext whose commands run
/// with <c>app.is_platform_admin = 'true'</c> set per-statement, allowing
/// the cross-tenant reads that the RLS policies otherwise deny.
///
/// <para>Lifecycle: caller MUST <c>await using</c> the returned
/// <see cref="BypassedDbContext{TContext}"/> wrapper. The wrapper disposes
/// the inner DbContext FIRST then pops the AsyncLocal bypass frame, so the
/// final outbox flush at dispose-time still runs under bypass.</para>
///
/// <para>Allowed call sites are enumerated in <c>docs/OPS_M_9_PLAN.md</c>
/// §7; the <c>RlsBypassCallSiteAllowlistTests</c> arch test pins the
/// allow-list. Adding a new bypass call site is a deliberate design
/// review.</para>
/// </summary>
public interface IRlsBypassDbContextFactory<TContext> where TContext : DbContext
{
    /// <summary>
    /// Opens a fresh bypass-flagged DbContext. The <paramref name="reason"/>
    /// is captured into a structured log line every invocation for
    /// after-the-fact audit. Treat it like a commit message — short,
    /// action-oriented, identifies the caller.
    /// </summary>
    Task<BypassedDbContext<TContext>> CreateForBypassAsync(string reason, CancellationToken ct = default);
}

/// <summary>
/// Slice OPS.M.9 §4.4 — separately-disposable wrapper returned by
/// <see cref="IRlsBypassDbContextFactory{TContext}"/>. Holds the inner
/// DbContext (accessed via <see cref="Db"/>) plus the AsyncLocal bypass
/// scope; disposal order is inner DbContext FIRST then the scope.
/// </summary>
public sealed class BypassedDbContext<TContext> : IAsyncDisposable, IDisposable
    where TContext : DbContext
{
    private readonly IDisposable _scope;
    private bool _disposed;

    internal BypassedDbContext(TContext db, IDisposable scope)
    {
        Db = db;
        _scope = scope;
    }

    public TContext Db { get; }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        await Db.DisposeAsync().ConfigureAwait(false);
        _scope.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Db.Dispose();
        _scope.Dispose();
    }
}
