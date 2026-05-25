using FluentValidation;
using MediatR;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Identity.Application.Behaviors;

namespace VrBook.Modules.Identity.Application.Users.Commands;

public sealed record UpdateProfileCommand(string DisplayName, string? Phone)
    : IRequest<UserDto>, IAuditable
{
    public string AuditAction => "user.update-profile";
    public string? AuditTargetType => "User";
    public string? AuditTargetId => null;
}

internal sealed class UpdateProfileValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileValidator()
    {
        RuleFor(c => c.DisplayName)
            .NotEmpty().WithMessage("Display name is required.")
            .MaximumLength(200);

        RuleFor(c => c.Phone)
            .MaximumLength(40)
            .Matches(@"^\+?[0-9\s\-()]{7,20}$").When(c => !string.IsNullOrWhiteSpace(c.Phone));
    }
}
