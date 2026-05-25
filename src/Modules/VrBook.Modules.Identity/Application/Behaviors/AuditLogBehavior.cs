using System.Diagnostics;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Identity.Domain;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Modules.Identity.Application.Behaviors;

/// <summary>
/// Writes an <see cref="AuditLogEntry"/> for every MediatR request that implements
/// <see cref="IAuditable"/>. Captures actor, target, before/after JSON, ip + UA + trace.
/// Failure to write the audit log MUST NOT fail the handler — it logs a warning instead.
/// See proposal §14.5.
/// </summary>
public sealed class AuditLogBehavior<TRequest, TResponse>(
    ICurrentUser currentUser,
    IdentityDbContext db,
    IHttpContextAccessor httpContext,
    ILogger<AuditLogBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IAuditable auditable)
        {
            return await next();
        }

        var beforeJson = SafeSerialize(request);
        TResponse response;
        try
        {
            response = await next();
        }
        catch
        {
            // Audit failed attempts too, but with action suffixed ".failed".
            TryWriteAudit(auditable, beforeJson, afterJson: null, suffix: ".failed");
            throw;
        }

        TryWriteAudit(auditable, beforeJson, SafeSerialize(response), suffix: null);
        return response;
    }

    private void TryWriteAudit(
        IAuditable auditable, string? beforeJson, string? afterJson, string? suffix)
    {
        try
        {
            var http = httpContext.HttpContext;
            var entry = AuditLogEntry.Record(
                actorUserId: currentUser.UserId,
                actorRole: ResolveRole(currentUser),
                action: auditable.AuditAction + (suffix ?? string.Empty),
                targetType: auditable.AuditTargetType,
                targetId: auditable.AuditTargetId,
                beforeJson: beforeJson,
                afterJson: afterJson,
                ip: http?.Connection.RemoteIpAddress?.ToString(),
                userAgent: http?.Request.Headers.UserAgent.ToString(),
                traceId: Activity.Current?.TraceId.ToString());

            db.AuditLog.Add(entry);
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Audit log write failed for {Action}", auditable.AuditAction);
        }
    }

    private static string ResolveRole(ICurrentUser u)
    {
        if (!u.IsAuthenticated)
        {
            return "anonymous";
        }

        if (u.IsAdmin)
        {
            return "admin";
        }

        if (u.IsOwner)
        {
            return "owner";
        }

        return "guest";
    }

    private static string? SafeSerialize(object? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Serialize(value, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
