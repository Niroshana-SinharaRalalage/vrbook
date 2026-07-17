using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Application.Common;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Modules.Identity.Application.Settings;

/// <summary>
/// VRB-211 — the settings "Recent changes" panel. Projects <c>identity.audit_log</c>
/// rows whose action is under the <c>settings.</c> prefix (optionally a single section,
/// or a single target such as a property) into <see cref="SettingsChangeDto"/>, newest
/// first, with the actor resolved to a display name. Owned by Identity because it owns
/// the audit log.
/// </summary>
public sealed record GetSettingsChangesQuery(string? Section = null, Guid? PropertyId = null, int Limit = 50)
    : IRequest<IReadOnlyList<SettingsChangeDto>>;

internal sealed class GetSettingsChangesHandler(IdentityDbContext db, IUserEmailLookup users)
    : IRequestHandler<GetSettingsChangesQuery, IReadOnlyList<SettingsChangeDto>>
{
    public async Task<IReadOnlyList<SettingsChangeDto>> Handle(GetSettingsChangesQuery request, CancellationToken cancellationToken)
    {
        var prefix = string.IsNullOrWhiteSpace(request.Section)
            ? SettingsAuditActions.Prefix
            : SettingsAuditActions.SectionPrefix(request.Section);

        var q = db.AuditLog.AsNoTracking().Where(a => a.Action.StartsWith(prefix));
        if (request.PropertyId is { } pid)
        {
            var target = pid.ToString();
            q = q.Where(a => a.TargetId == target);
        }

        var limit = Math.Clamp(request.Limit, 1, 200);
        var rows = await q
            .OrderByDescending(a => a.OccurredAt)
            .Take(limit)
            .Select(a => new { a.ActorUserId, a.ActorRole, a.Action, a.Before, a.After, a.OccurredAt })
            .ToListAsync(cancellationToken);

        // Resolve actor display names via the shared lookup (small panel; few distinct actors).
        var actorIds = rows.Where(r => r.ActorUserId is not null).Select(r => r.ActorUserId!.Value).Distinct();
        var names = new Dictionary<Guid, string>();
        foreach (var id in actorIds)
        {
            var snap = await users.GetAsync(id, cancellationToken);
            if (snap is not null)
            {
                names[id] = snap.DisplayName is { Length: > 0 } dn ? dn : snap.Email;
            }
        }

        return rows.Select(r => new SettingsChangeDto(
            Actor: r.ActorUserId is { } id && names.TryGetValue(id, out var n) ? n : r.ActorRole,
            Action: r.Action,
            Before: r.Before,
            After: r.After,
            At: r.OccurredAt)).ToList();
    }
}
