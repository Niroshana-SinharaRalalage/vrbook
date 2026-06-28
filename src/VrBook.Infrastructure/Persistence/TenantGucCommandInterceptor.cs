using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Interfaces;

namespace VrBook.Infrastructure.Persistence;

/// <summary>
/// Slice OPS.M.9 §4.3 (D3) — DbCommandInterceptor that stamps two
/// PostgreSQL GUCs per command:
/// <list type="bullet">
///   <item><c>app.tenant_id</c> — the caller's tenant id (string-formatted
///         Guid <c>D</c> format) or empty string if anonymous.</item>
///   <item><c>app.is_platform_admin</c> — <c>'true'</c> iff an
///         <see cref="RlsBypassScope"/> is active on the logical async thread.</item>
/// </list>
///
/// <para>Resolution order for <c>app.tenant_id</c>:
/// <list type="number">
///   <item><see cref="ICurrentUser.TenantId"/> (HTTP request path).</item>
///   <item><see cref="BackgroundTenantScope.CurrentTenantId"/> (worker /
///         background-command path, set by the OPS.M.6 behavior).</item>
///   <item>Empty string (fail-safe — every RLS policy denies).</item>
/// </list>
/// </para>
///
/// <para>The GUC is set via <c>set_config('name', value, true)</c> — the
/// <c>true</c> argument makes it a <c>SET LOCAL</c> equivalent, scoped to
/// the current transaction. EF Core wraps SaveChangesAsync in a
/// transaction; for SELECT outside an explicit transaction, the implicit
/// statement-level transaction is the scope.</para>
/// </summary>
public sealed class TenantGucCommandInterceptor : DbCommandInterceptor
{
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<TenantGucCommandInterceptor> _logger;

    public TenantGucCommandInterceptor(
        ICurrentUser currentUser, ILogger<TenantGucCommandInterceptor> logger)
    {
        _currentUser = currentUser;
        _logger = logger;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        await StampTenantGucsAsync(command, cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await StampTenantGucsAsync(command, cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData,
        InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        await StampTenantGucsAsync(command, cancellationToken);
        return result;
    }

    private async Task StampTenantGucsAsync(DbCommand command, CancellationToken ct)
    {
        var bypass = RlsBypassScope.IsActive;

        // Resolution order per §4.3: ICurrentUser > BackgroundTenantScope > empty.
        var tenantId = _currentUser.TenantId ?? BackgroundTenantScope.CurrentTenantId;
        var tenantGuc = tenantId?.ToString("D") ?? string.Empty;
        var bypassGuc = bypass ? "true" : "false";

        if (command.Connection is null || command.Connection.State != ConnectionState.Open)
        {
            // Defensive — should never hit; EF opens before invoking the interceptor.
            return;
        }

        using var setCmd = command.Connection.CreateCommand();
        setCmd.Transaction = command.Transaction;
        setCmd.CommandText =
            "SELECT set_config('app.tenant_id', @tenant_id, true), " +
            "       set_config('app.is_platform_admin', @bypass_flag, true);";

        var tenantParam = setCmd.CreateParameter();
        tenantParam.ParameterName = "@tenant_id";
        tenantParam.Value = tenantGuc;
        setCmd.Parameters.Add(tenantParam);

        var bypassParam = setCmd.CreateParameter();
        bypassParam.ParameterName = "@bypass_flag";
        bypassParam.Value = bypassGuc;
        setCmd.Parameters.Add(bypassParam);

        await setCmd.ExecuteNonQueryAsync(ct);

        _logger.LogDebug(
            "RLS GUC stamped tenant_id={TenantId} is_platform_admin={Bypass}",
            string.IsNullOrEmpty(tenantGuc) ? "<empty>" : tenantGuc, bypassGuc);
    }
}
