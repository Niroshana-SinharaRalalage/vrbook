namespace VrBook.Infrastructure.Persistence;

/// <summary>
/// Slice OPS.M.9 §4.5 (D5) — AsyncLocal carry for the background-worker
/// tenant id when no <c>ICurrentUser</c> is available.
///
/// <para>The OPS.M.6 <c>BackgroundCommandTenantScopeBehavior</c> opens a
/// scope at the start of every <c>IBackgroundCommand</c> handler; the
/// <see cref="TenantGucCommandInterceptor"/> reads
/// <see cref="CurrentTenantId"/> as the fallback when
/// <c>ICurrentUser.TenantId</c> is null. The interceptor stamps that value
/// into <c>app.tenant_id</c> for every command issued inside the scope.</para>
///
/// <para>If no scope is active and <c>ICurrentUser.TenantId</c> is null,
/// the interceptor stamps an empty string — the RLS policy denies every
/// row, which is the desired fail-safe.</para>
/// </summary>
public static class BackgroundTenantScope
{
    private static readonly AsyncLocal<Stack<Guid>?> _stack = new();

    /// <summary>
    /// The most recently entered scope's tenant id, or <c>null</c> if no
    /// scope is active on the logical async thread.
    /// </summary>
    public static Guid? CurrentTenantId =>
        _stack.Value is { Count: > 0 } s ? s.Peek() : null;

    /// <summary>
    /// Push a new background-tenant frame. Returns an
    /// <see cref="IDisposable"/> that pops the frame on dispose.
    /// </summary>
    public static IDisposable Enter(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Background tenant scope requires a non-empty tenant id.", nameof(tenantId));
        }
        var stack = _stack.Value ??= new Stack<Guid>();
        stack.Push(tenantId);
        return new ExitScope();
    }

    private sealed class ExitScope : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (_stack.Value is { Count: > 0 } s)
            {
                s.Pop();
            }
        }
    }
}
