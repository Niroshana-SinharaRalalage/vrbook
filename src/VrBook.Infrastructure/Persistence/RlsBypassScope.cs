namespace VrBook.Infrastructure.Persistence;

/// <summary>
/// Slice OPS.M.9 §4.4 (D4) — AsyncLocal stack holding the "bypass-active"
/// flag for the RLS interceptor.
///
/// <para>The <see cref="IRlsBypassDbContextFactory{TContext}"/> opens a
/// scope before constructing the DbContext; the
/// <see cref="TenantGucCommandInterceptor"/> reads <see cref="IsActive"/>
/// on every command and stamps <c>app.is_platform_admin = 'true'</c> when
/// it's set. Nested scopes are supported via a depth counter so a bypass
/// inside a bypass (e.g. one factory call inside another's <c>using</c>
/// block) still composes correctly.</para>
///
/// <para>Logical-thread-scoped (AsyncLocal): the flag flows across awaits
/// in the same async chain. Callers must keep bypass contexts short-lived
/// per OPS.M.9 §9 — open → query → dispose — and MUST NOT hold across an
/// unrelated await that might cause an unrelated DbContext command to run
/// under the bypass.</para>
/// </summary>
public static class RlsBypassScope
{
    private static readonly AsyncLocal<int> _depth = new();

    /// <summary>
    /// True iff at least one <see cref="Enter"/> scope is currently active
    /// on the logical async thread.
    /// </summary>
    public static bool IsActive => _depth.Value > 0;

    /// <summary>
    /// Push a new bypass frame. Returns an <see cref="IDisposable"/> that
    /// pops the frame on dispose. Idempotent on disposal — calling
    /// <see cref="IDisposable.Dispose"/> twice is a no-op.
    /// </summary>
    public static IDisposable Enter()
    {
        _depth.Value++;
        return new ExitScope();
    }

    private sealed class ExitScope : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _depth.Value = Math.Max(0, _depth.Value - 1);
        }
    }
}
