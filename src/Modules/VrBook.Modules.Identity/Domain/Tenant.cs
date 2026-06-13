using VrBook.Domain.Common;

namespace VrBook.Modules.Identity.Domain;

/// <summary>
/// Slice 3 placeholder for the full Tenant aggregate that OPS.M.1 introduces.
/// Exists now so Slice 3..7 tables can ship with <c>tenant_id uuid NULL REFERENCES
/// identity.tenants(id)</c> from their first migration — see REPLAN.md §10.1.
/// The full shape (status, default_currency, default_timezone, support_email,
/// platform_fee_bps, stripe_account_id, etc.) lands in OPS.M.1; do not extend here.
/// </summary>
public sealed class Tenant : AggregateRoot
{
    public string Slug { get; private set; } = default!;
    public string DisplayName { get; private set; } = default!;

    private Tenant() { }   // EF Core

    public static Tenant Create(string slug, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        return new Tenant
        {
            Id = Guid.NewGuid(),
            Slug = slug.Trim().ToLowerInvariant(),
            DisplayName = displayName.Trim(),
        };
    }
}
