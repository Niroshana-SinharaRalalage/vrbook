namespace VrBook.Api.IntegrationTests.Auth;

/// <summary>
/// Slice OPS.M.14.1 — one row in a fixture's persona lookup. Consumed by
/// <see cref="TestAuthHandler"/> to synthesize a <c>ClaimsPrincipal</c> that
/// matches the shape of an Entra External ID access token (oid, emails,
/// email_verified, name, extension_isOwner, extension_isAdmin).
/// </summary>
public sealed record TestPersona(
    string Oid,
    string Email,
    string DisplayName,
    bool IsOwner,
    bool IsAdmin);
