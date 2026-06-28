using MediatR;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;

namespace VrBook.Modules.Sync.Application.Behaviors;

/// <summary>
/// OPS.M.6 §3.1 (D1) — paired with <c>TenantAuthorizationBehavior</c>'s
/// early-return for <see cref="IBackgroundCommand"/>. For background-origin
/// requests, this behavior:
/// <list type="number">
///   <item>Asserts <c>TenantId != Guid.Empty</c> — a worker that forgot to
///         stamp the tenant id from the row would otherwise leak across
///         tenants on writes. Throws
///         <c>sync.background_command_unstamped</c> on failure.</item>
///   <item>Pushes <c>tenant_id</c> into the logger's scope so every
///         downstream log line carries the routing key.</item>
/// </list>
/// </summary>
public sealed class BackgroundCommandTenantScopeBehavior<TRequest, TResponse>(
    ILogger<BackgroundCommandTenantScopeBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IBackgroundCommand)
        {
            return await next();
        }
        if (request is not ITenantScoped scoped)
        {
            // Architecture invariant — caught by BackgroundCommandMarkerTests.
            // Belt-and-braces here so a runtime regression also faults loudly.
            throw new BusinessRuleViolationException(
                "sync.background_command_unscoped",
                $"{typeof(TRequest).Name} implements IBackgroundCommand but not ITenantScoped.");
        }
        if (scoped.TenantId == Guid.Empty)
        {
            throw new BusinessRuleViolationException(
                "sync.background_command_unstamped",
                $"{typeof(TRequest).Name} arrived at the handler with TenantId == Guid.Empty. " +
                "The worker must stamp the tenant id from the row it's processing.");
        }
        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["tenant_id"] = scoped.TenantId,
            ["request_type"] = typeof(TRequest).Name,
        }))
        {
            return await next();
        }
    }
}
