namespace VrBook.Contracts.Dtos;

/// <summary>Public-shape of an authenticated user. See proposal §6.2 — GET /me.</summary>
public sealed record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string? Phone,
    bool IsOwner,
    bool IsAdmin,
    bool EmailVerified,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

public sealed record UpdateProfileRequest(
    string DisplayName,
    string? Phone);
