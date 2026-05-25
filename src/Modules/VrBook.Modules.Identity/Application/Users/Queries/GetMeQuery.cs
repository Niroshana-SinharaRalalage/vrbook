using MediatR;
using VrBook.Contracts.Dtos;

namespace VrBook.Modules.Identity.Application.Users.Queries;

public sealed record GetMeQuery : IRequest<UserDto>;
