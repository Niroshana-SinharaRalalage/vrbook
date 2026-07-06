namespace VrBook.Contracts.Common;

/// <summary>
/// Stable type URIs used in RFC 7807 ProblemDetails responses. Clients can switch on these.
/// </summary>
public static class ProblemTypes
{
    public const string Base = "https://vrbook.example.com/problems";

    public const string Validation = $"{Base}/validation";
    public const string NotFound = $"{Base}/not-found";
    public const string Conflict = $"{Base}/conflict";
    public const string Unauthorized = $"{Base}/unauthorized";
    public const string Forbidden = $"{Base}/forbidden";
    public const string RateLimited = $"{Base}/rate-limited";
    public const string IdempotencyReuse = $"{Base}/idempotency-key-reused-with-different-body";
    public const string PaymentRequired = $"{Base}/payment-required";
    public const string UpstreamFailure = $"{Base}/upstream-failure";
    public const string Internal = $"{Base}/internal-server-error";

    /// <summary>
    /// Slice OPS.M.12 — surface for the middleware admin-vs-social gate.
    /// A social-IdP token attempting to exercise admin authority
    /// (IsPlatformAdmin or any tenant membership) hits this shape.
    /// </summary>
    public const string AdminSocialIdpRejected = $"{Base}/admin-social-idp-rejected";
}
