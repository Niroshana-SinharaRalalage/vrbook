using VrBook.Contracts.Dtos;
using VrBook.Modules.Identity.Domain;

namespace VrBook.Modules.Identity.Application.Users.Common;

internal static class UserMapping
{
    public static UserDto ToDto(this User u) => new(
        Id: u.Id,
        Email: u.Email.Value,
        DisplayName: u.DisplayName,
        Phone: u.Phone.IsEmpty ? null : u.Phone.Value,
        IsOwner: u.IsOwner,
        IsAdmin: u.IsAdmin,
        IsPlatformAdmin: u.IsPlatformAdmin,
        EmailVerified: u.EmailVerified,
        CreatedAt: u.CreatedAt,
        LastLoginAt: u.LastLoginAt);
}
