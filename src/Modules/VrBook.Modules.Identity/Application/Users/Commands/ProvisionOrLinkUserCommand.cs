using MediatR;
using VrBook.Application.Common;

namespace VrBook.Modules.Identity.Application.Users.Commands;

/// <summary>
/// Slice OPS.M.13 — supersedes the retired <c>ProvisionUserCommand</c> per
/// <c>docs/OPS_M_13_IDENTITY_REDESIGN_PLAN.md</c> §2.2.
///
/// <para>Issued by <c>UserProvisioningMiddleware</c> on every
/// authenticated request. The handler runs an email-first algorithm:
/// find <c>user_identities</c> by <c>(provider, external_id)</c> →
/// if miss, find <c>users</c> by <c>lower(email)</c> → link if
/// verified, throw <c>email_unverified_cannot_bind_profile</c> if
/// unverified → if email miss, provision fresh users + first identity.
/// See handler for full algorithm + race handling.</para>
/// </summary>
public sealed record ProvisionOrLinkUserCommand(
    string Provider,        // 'entra' today; 'google'/'microsoft' post-M.12.
    string ExternalId,      // The oid claim (or Google 'sub', etc.).
    string Email,
    bool EmailVerified,
    string DisplayName) : IRequest<Guid>, IAuditable
{
    public string AuditAction => "user.provision-or-link";
    public string? AuditTargetType => "User";
    public string? AuditTargetId => $"{Provider}:{ExternalId}";
}
