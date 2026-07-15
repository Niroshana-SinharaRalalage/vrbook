using MediatR;
using VrBook.Application.Common;

namespace VrBook.Modules.Identity.Application.Users.Commands;

public sealed record DeactivateMeCommand(string Reason = "User-initiated deactivation.")
    : IRequest, IAuditable
{
    public string AuditAction => "user.self-deactivate";
    public string? AuditTargetType => "User";
    public string? AuditTargetId => null;
}
