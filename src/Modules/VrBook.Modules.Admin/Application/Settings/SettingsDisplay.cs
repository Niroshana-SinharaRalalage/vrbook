using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Admin.Domain;

namespace VrBook.Modules.Admin.Application.Settings;

/// <summary>VRB-216 — maps settings rows to their read DTOs, resolving the "last changed
/// by" display name from the actor user id (email/display-name, or the raw id fallback).</summary>
internal static class SettingsDisplay
{
    public static async Task<string?> ResolveActorAsync(Guid userId, IUserEmailLookup users, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            return null;
        }
        var snap = await users.GetAsync(userId, cancellationToken);
        return snap?.DisplayName ?? snap?.Email ?? userId.ToString();
    }

    public static async Task<GlobalCancellationTiersDto> MapTiersAsync(
        CancellationTiers row, IUserEmailLookup users, CancellationToken cancellationToken) =>
        new(
            row.FirstTierDays,
            row.SecondTierDays,
            row.MiddleTierRefundPct,
            row.FinalCutoffHours,
            row.UpgradePricePct,
            row.Version,
            await ResolveActorAsync(row.UpdatedByUserId, users, cancellationToken),
            row.UpdatedAt);
}
