using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VrBook.Domain.Common;
using VrBook.Modules.Identity.Application.Users.Commands;
using VrBook.Modules.Identity.Domain;
using VrBook.Modules.Identity.Infrastructure.Persistence;
using VrBook.Modules.Identity.Options;

namespace VrBook.Modules.Identity.Infrastructure.Auth;

/// <summary>
/// On every authenticated request, ensures a row exists in <c>identity.users</c> for the
/// caller's external identity (Entra oid + provider) via
/// <c>ProvisionOrLinkUserCommand</c>. Also stamps <c>HttpContext.Items</c> with the
/// app-side user id + platform-admin flag + resolved active tenant + per-tenant
/// role dictionary. Downstream handlers read these through <c>ICurrentUser</c>.
///
/// <para>Slice OPS.M.13.6 — active-tenant resolution flipped from
/// "always the caller's IsPrimary membership" to "X-Active-Tenant header if
/// present and valid, else IsPrimary membership fallback". The tenant-picker
/// SPA sets the header from sessionStorage on every non-anonymous request;
/// non-SPA callers (curl, integration tests) hit the fallback path.</para>
///
/// <para>Slice OPS.M.13.6 — the per-membership <c>ClaimTypes.Role</c> loop
/// was dropped. Downstream tenant-role checks read
/// <c>ICurrentUser.MembershipRoles</c> which is stamped as a
/// <c>Dictionary&lt;Guid, IReadOnlySet&lt;string&gt;&gt;</c> on
/// <c>HttpContext.Items</c>. The <c>PlatformAdmin</c> role claim IS still
/// written because <c>[Authorize(Roles="PlatformAdmin")]</c> reads through
/// <c>ClaimsPrincipal.IsInRole</c>.</para>
/// </summary>
public sealed class UserProvisioningMiddleware(RequestDelegate next, ILogger<UserProvisioningMiddleware> logger)
{
    /// <summary>
    /// Slice OPS.M.22 §6 — request paths where the admin-preseed gate is
    /// bypassed. The SPA needs <c>/api/v1/me</c> + <c>/api/v1/me/tenants</c>
    /// to render the rejection page (M.22.7) even for a token whose email
    /// hasn't been operator-provisioned. Matches the M.12 Layer 2 pattern
    /// in <c>AdminSocialIdpRejectionMiddleware</c>.
    /// </summary>
    private static readonly PathString MePath = new("/api/v1/me");
    private static readonly PathString MeTenantsPath = new("/api/v1/me/tenants");

    public async Task InvokeAsync(HttpContext ctx, IMediator mediator, IdentityDbContext db, IConfiguration configuration, IOptions<EntraExternalIdOptions> entraOptions)
    {
        if (ctx.User?.Identity?.IsAuthenticated == true)
        {
            var oid = ctx.User.FindFirstValue("oid") ?? ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(oid))
            {
                try
                {
                    // Slice OPS.M.13.6 walk fix — broadened claim-name coverage.
                    // Entra External ID user flows that don't include email
                    // claim in the "Application Claims" config emit the user's
                    // email in preferred_username / upn instead. Order goes
                    // most-specific → most-fallback; the synthetic
                    // '@unknown.local' is a last-resort that must never win
                    // when a real email claim is available (previous
                    // signature-ordering bug shipped rows keyed on the fake
                    // email; see LA trace 3c4bff266643b848dec1b075a9c9a5b3).
                    //
                    // 2026-07-08 fix — CIAM federated Google sign-in path:
                    // preferred_username carries the SYNTHETIC UPN
                    // `{oid}@<tenant>.onmicrosoft.com`, NOT the real Google
                    // email. The real email lives in either `email` claim
                    // (when the `email` scope is requested and the mail
                    // attribute is populated) OR `verified_primary_email`
                    // (CIAM's authoritative primary email, sourced from the
                    // user's PrimaryAuthoritativeEmail attribute). Both are
                    // now checked BEFORE preferred_username so a federated
                    // guest never lands with a synthetic UPN as email.
                    var rawEmail = ctx.User.FindFirstValue("emails")
                                   ?? ctx.User.FindFirstValue("email")
                                   ?? ctx.User.FindFirstValue(ClaimTypes.Email)
                                   ?? ctx.User.FindFirstValue("verified_primary_email")
                                   ?? ctx.User.FindFirstValue("preferred_username")
                                   ?? ctx.User.FindFirstValue("upn");
                    // Reject synthetic-looking values (e.g. a raw oid mistakenly
                    // put in preferred_username) before falling to the last
                    // resort. If we still don't have anything, use the synthetic
                    // but log at Warning so the token-config gap is visible.
                    string email;
                    if (!string.IsNullOrWhiteSpace(rawEmail) && rawEmail.Contains('@', StringComparison.Ordinal))
                    {
                        email = rawEmail;
                    }
                    else
                    {
                        email = $"{oid}@unknown.local";
                        logger.LogWarning(
                            "Entra token missing email/emails/preferred_username/upn claim for oid {Oid}. Falling back to synthetic '{Email}'. " +
                            "Fix at Entra user flow → Application Claims (add Email + Email Verified).",
                            oid, email);
                    }

                    var displayName = ctx.User.FindFirstValue("name")
                                      ?? ctx.User.FindFirstValue(ClaimTypes.Name)
                                      ?? "User";

                    // Slice OPS.M.13.6 walk fix — email_verified handling.
                    // Entra External ID's built-in local-account signup flow
                    // requires OTP email verification before the account
                    // becomes active. So when we see provider='entra' and no
                    // explicit email_verified claim, treat it as verified
                    // (verified => the signup completed). If the claim IS
                    // present, respect its value (federated Google could set
                    // it to false for un-verified Google emails). Federated
                    // providers land in OPS.M.12 with per-provider policy.
                    var emailVerifiedRaw = ctx.User.FindFirstValue("email_verified");
                    var emailVerified = emailVerifiedRaw is null
                        || string.Equals(emailVerifiedRaw, "true", StringComparison.OrdinalIgnoreCase);

                    // Slice OPS.M.12.3 — classify the identity provider from
                    // the JWT `idp` claim. Entra-local sign-ins (no idp or
                    // idp = tenant issuer host) → "entra". Social federation
                    // → "google" / "microsoft" / "facebook" / "apple".
                    // Unknown values pass through verbatim and hit the DB
                    // CHECK constraint (loud failure > silent misclassify).
                    var idpClaim = ctx.User.FindFirstValue(HttpCurrentUser.IdpClaim);
                    var tenantIssuerHost = configuration["EntraExternalId:TenantIssuerHost"];
                    var provider = IdentityProviderClassifier.Classify(idpClaim, tenantIssuerHost);

                    // Slice OPS.M.22 §3-§6 admin-preseed gate.
                    // Read the CIAM flow marker (tfp first, acr fallback) and
                    // compare against the configured admin flow name. If the
                    // token came from AdminSignUpSignIn AND the email is
                    // unknown (or matches a NON-pre-seeded row) → refuse with
                    // AdminAccountNotProvisionedException. The whitelist
                    // (/api/v1/me + /api/v1/me/tenants) skips the throw so
                    // the SPA's rejection page (M.22.7) can render — else the
                    // SPA hits a 401 on its very first call and can't parse
                    // the problem-type payload.
                    //
                    // Guest-flow tokens AND legacy tokens with no flow marker
                    // fall through to the unchanged Branch 3 lazy-provision
                    // path — self-serve guest signup is unaffected. Owner-
                    // locked in plan §5-Q1: admins pre-seeded, guests
                    // self-serve.
                    // VRB-209 (G7) — read from the bound + validated options (was a raw
                    // configuration[...] read of an unprovided key).
                    var adminFlowName = entraOptions.Value.AdminFlowName;
                    var tokenFlow = ctx.User.FindFirstValue(HttpCurrentUser.EntraFlowTfpClaim)
                                    ?? ctx.User.FindFirstValue(HttpCurrentUser.EntraFlowAcrClaim);
                    var isAdminFlow = !string.IsNullOrWhiteSpace(adminFlowName)
                                      && !string.IsNullOrWhiteSpace(tokenFlow)
                                      && string.Equals(tokenFlow, adminFlowName, StringComparison.OrdinalIgnoreCase);

                    if (isAdminFlow)
                    {
                        var normalizedEmail = email.Trim().ToLowerInvariant();
                        var preSeededHit = await db.Users
                            .Where(u => EF.Functions.ILike(((string)(object)u.Email), normalizedEmail)
                                        && u.PreSeededAt != null)
                            .Select(u => u.Id)
                            .FirstOrDefaultAsync(ctx.RequestAborted);

                        if (preSeededHit == Guid.Empty)
                        {
                            // Path whitelist: /me + /me/tenants stay reachable
                            // so the SPA rejection page can render (M.22.7).
                            // For every other admin path, throw 401.
                            var reqPath = ctx.Request.Path;
                            var whitelisted = reqPath.StartsWithSegments(MeTenantsPath, StringComparison.OrdinalIgnoreCase)
                                              || reqPath.Equals(MePath, StringComparison.OrdinalIgnoreCase);
                            if (!whitelisted)
                            {
                                logger.LogWarning(
                                    "OPS.M.22 admin-preseed gate fired. Email={Email} Oid={Oid} Flow={Flow} Path={Path}",
                                    email, oid, tokenFlow, reqPath.Value);
                                throw new AdminAccountNotProvisionedException(email, oid);
                            }

                            // Whitelisted path — do NOT provision, do NOT
                            // stamp AppUserId. Downstream reads ICurrentUser
                            // as anonymous-shaped so /me can shape a response
                            // the SPA can interpret (M.22.7 renders on it).
                            await next(ctx);
                            return;
                        }
                    }

                    var userId = await mediator.Send(new ProvisionOrLinkUserCommand(
                        Provider: provider,
                        ExternalId: oid,
                        Email: email,
                        EmailVerified: emailVerified,
                        DisplayName: displayName));

                    ctx.Items[HttpCurrentUser.AppUserIdItemKey] = userId;

                    var memberships = await db.Set<TenantMembership>()
                        .Where(m => m.UserId == userId && m.DeletedAt == null)
                        .Select(m => new { m.TenantId, m.Role, m.IsPrimary })
                        .ToListAsync(ctx.RequestAborted);

                    var isPlatformAdmin = await db.Users
                        .Where(u => u.Id == userId)
                        .Select(u => u.IsPlatformAdmin)
                        .FirstOrDefaultAsync(ctx.RequestAborted);

                    ctx.Items[HttpCurrentUser.PlatformAdminItemKey] = isPlatformAdmin;

                    // Slice OPS.M.13.6 — build the per-tenant role dictionary and stamp
                    // Items[MembershipRoles]. Multiple memberships in the same tenant
                    // (rare — shouldn't happen given the (user_id, tenant_id) partial
                    // UNIQUE — but defense in depth) get their role sets merged.
                    var membershipRoles = memberships
                        .GroupBy(m => m.TenantId)
                        .ToDictionary<IGrouping<Guid, dynamic>, Guid, IReadOnlySet<string>>(
                            g => g.Key,
                            g => (IReadOnlySet<string>)new HashSet<string>(
                                g.Select(m => (string)m.Role),
                                StringComparer.Ordinal));
                    ctx.Items[HttpCurrentUser.MembershipRolesItemKey] = membershipRoles;

                    // Slice OPS.M.13.6 — active tenant resolution.
                    // Priority: X-Active-Tenant header (SPA) → IsPrimary fallback (curl + tests).
                    Guid? activeTenantId = null;
                    var headerValue = ctx.Request.Headers[HttpCurrentUser.ActiveTenantHeader]
                        .ToString();
                    if (!string.IsNullOrWhiteSpace(headerValue)
                        && Guid.TryParse(headerValue, out var headerTenantId)
                        && memberships.Any(m => m.TenantId == headerTenantId))
                    {
                        activeTenantId = headerTenantId;
                    }
                    else
                    {
                        var primary = memberships.FirstOrDefault(m => m.IsPrimary);
                        if (primary is not null)
                        {
                            activeTenantId = primary.TenantId;
                        }
                    }

                    if (activeTenantId.HasValue)
                    {
                        ctx.Items[HttpCurrentUser.ActiveTenantItemKey] = activeTenantId.Value;
                    }

                    // Slice OPS.M.13.6 — write ONLY the PlatformAdmin role claim +
                    // the legacy app_tenant_id claim (still consumed by pre-M.13
                    // code paths + any external audit). Per-membership role claims
                    // are gone; HasTenantRole reads MembershipRoles from Items.
                    if ((activeTenantId.HasValue || isPlatformAdmin)
                        && ctx.User.Identity is ClaimsIdentity primaryIdentity)
                    {
                        if (isPlatformAdmin)
                        {
                            primaryIdentity.AddClaim(new Claim(
                                ClaimTypes.Role, HttpCurrentUser.PlatformAdminRole));
                        }

                        if (activeTenantId.HasValue)
                        {
                            primaryIdentity.AddClaim(new Claim(
                                HttpCurrentUser.TenantIdClaimType, activeTenantId.Value.ToString()));
                        }
                    }
                }
                catch (AdminAccountNotProvisionedException)
                {
                    // Slice OPS.M.22 §6 — must propagate to the ProblemDetails
                    // mapper. Swallowing it here (as the generic catch does
                    // for defensiveness) would drop the 401 shape and let the
                    // request continue as anonymous, breaking the admin-gate
                    // invariant.
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "User provisioning failed for oid {Oid}. Request continues without app-user id.", oid);
                }
            }
        }

        await next(ctx);
    }
}
