namespace VrBook.Modules.Identity.Infrastructure.Auth;

/// <summary>
/// Slice OPS.M.12.3 — maps a JWT <c>idp</c> claim value (Entra External ID's
/// canonical per-provider host string) to the canonical
/// <c>identity.user_identities.provider</c> column value the M.13 handler
/// writes to the DB.
///
/// <para>Consumed by <c>UserProvisioningMiddleware</c> to convert the token's
/// idp claim into the string that goes into
/// <c>ProvisionOrLinkUserCommand.Provider</c>. Also consumed by
/// <see cref="HttpCurrentUser"/> constant tests to keep both classifiers
/// aligned.</para>
///
/// <para>The mapping is intentionally lossy for the OUTPUT (canonical
/// tokens: entra / google / microsoft / facebook / apple) but PRESERVES
/// unknown IdP claims verbatim so a future portal-add hits the DB CHECK
/// constraint (visible failure) rather than silently collapsing to
/// entra (invisible failure).</para>
/// </summary>
public static class IdentityProviderClassifier
{
    /// <summary>
    /// Map the <c>idp</c> claim value → canonical
    /// <c>user_identities.provider</c> string.
    /// </summary>
    /// <param name="idpClaim">Raw <c>idp</c> claim from the JWT. May be null / whitespace.</param>
    /// <param name="entraTenantIssuerHost">
    /// The Entra External ID tenant's issuer host (e.g. <c>vrbook.ciamlogin.com</c>
    /// or <c>&lt;tenantId&gt;.ciamlogin.com</c>). When the idp claim equals this
    /// host, the caller is signed in via Entra local — classified as
    /// <c>"entra"</c>. May be null/empty when config is missing; falls through.
    /// </param>
    /// <returns>
    /// One of <c>"entra"</c>, <c>"google"</c>, <c>"microsoft"</c>,
    /// <c>"facebook"</c>, <c>"apple"</c>, or the raw literal claim value
    /// (which will fail the DB CHECK if it's not in the constraint set).
    /// </returns>
    public static string Classify(string? idpClaim, string? entraTenantIssuerHost)
    {
        if (string.IsNullOrWhiteSpace(idpClaim))
        {
            return HttpCurrentUser.ProviderEntraLocal;
        }

        if (!string.IsNullOrWhiteSpace(entraTenantIssuerHost)
            && string.Equals(idpClaim, entraTenantIssuerHost, StringComparison.OrdinalIgnoreCase))
        {
            return HttpCurrentUser.ProviderEntraLocal;
        }

        if (string.Equals(idpClaim, "google.com", StringComparison.OrdinalIgnoreCase))
        {
            return HttpCurrentUser.ProviderGoogle;
        }

        if (string.Equals(idpClaim, "live.com", StringComparison.OrdinalIgnoreCase))
        {
            return HttpCurrentUser.ProviderMicrosoft;
        }

        if (string.Equals(idpClaim, "facebook.com", StringComparison.OrdinalIgnoreCase))
        {
            return HttpCurrentUser.ProviderFacebook;
        }

        if (string.Equals(idpClaim, "apple.com", StringComparison.OrdinalIgnoreCase))
        {
            return HttpCurrentUser.ProviderApple;
        }

        // Unknown IdP shape (linkedin.com / twitter.com / amazon.com / etc.)
        // Preserve verbatim. DB CHECK constraint will reject it if it's
        // not in ('entra','google','microsoft','apple','facebook','test').
        // Failure is loud (23514) rather than silent.
        return idpClaim;
    }
}
