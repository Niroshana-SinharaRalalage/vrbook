using MediatR;
using VrBook.Modules.Identity.Application.Behaviors;

namespace VrBook.Modules.Identity.Application.Users.Commands;

/// <summary>
/// Issued by <c>UserProvisioningMiddleware</c> on first-login per <c>oid</c>.
/// Idempotent on repeat — if a user already exists for that <c>oid</c>, just refresh
/// LastLoginAt + DisplayName + EmailVerified from the latest claims.
/// </summary>
public sealed record ProvisionUserCommand(
    string B2CObjectId,
    string Email,
    string DisplayName,
    bool EmailVerified,
    bool IsOwner,
    bool IsAdmin) : IRequest<Guid>, IAuditable
{
    public string AuditAction => "user.provision-from-b2c";
    public string? AuditTargetType => "User";
    public string? AuditTargetId => B2CObjectId;
}
