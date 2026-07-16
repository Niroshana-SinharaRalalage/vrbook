using Microsoft.Extensions.Options;
using VrBook.Modules.Catalog.Options;
using VrBook.Modules.Identity.Options;
using VrBook.Modules.Notifications.Options;
using VrBook.Modules.Payment.Application;
using VrBook.Modules.Payment.Infrastructure.Stripe;

namespace VrBook.Api.Configuration;

// VRB-200 — cross-field IValidateOptions validators. Each failure message names
// the exact Section:Key + the rule it violated so a misconfigured deploy fails
// loudly with an actionable message (AC: "the failure message names the exact
// Section:Key and the rule violated"). Registered only in Staging/Production for
// EntraExternalId (dev-loopback carve-out); always for the rest.

/// <summary>Entra required-presence gate. Only registered outside Development —
/// a missing value here must crash the host rather than boot with JwtBearer
/// unwired (gap G5).</summary>
internal sealed class EntraExternalIdOptionsValidator : IValidateOptions<EntraExternalIdOptions>
{
    public ValidateOptionsResult Validate(string? name, EntraExternalIdOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Instance))
        {
            failures.Add("EntraExternalId:Instance is required (Staging/Production) — without it JwtBearer is unwired and the API accepts unvalidated tokens.");
        }
        if (string.IsNullOrWhiteSpace(options.TenantId))
        {
            failures.Add("EntraExternalId:TenantId is required (Staging/Production).");
        }
        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add("EntraExternalId:ClientId is required (Staging/Production).");
        }
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>Stripe: empty <c>SecretKey</c> is payment-disabled mode (valid in
/// every env). When a secret is set the webhook secret must also be present so
/// signature verification is never silently off.</summary>
internal sealed class StripeOptionsValidator : IValidateOptions<StripeOptions>
{
    public ValidateOptionsResult Validate(string? name, StripeOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.SecretKey) &&
            string.IsNullOrWhiteSpace(options.WebhookSecret))
        {
            return ValidateOptionsResult.Fail(
                "Stripe:WebhookSecret is required when Stripe:SecretKey is set (webhook signature verification would otherwise be off).");
        }
        return ValidateOptionsResult.Success;
    }
}

/// <summary>Refund service-fee percent must be in [0, 100].</summary>
internal sealed class RefundOptionsValidator : IValidateOptions<RefundOptions>
{
    public ValidateOptionsResult Validate(string? name, RefundOptions options)
    {
        if (options.ServiceFeePercent < 0m || options.ServiceFeePercent > 100m)
        {
            return ValidateOptionsResult.Fail(
                $"Refund:ServiceFeePercent must be between 0 and 100 (was {options.ServiceFeePercent}).");
        }
        return ValidateOptionsResult.Success;
    }
}

/// <summary>ACS: a connection string implies email is enabled, which requires a
/// valid sender address.</summary>
internal sealed class AcsOptionsValidator : IValidateOptions<AcsOptions>
{
    public ValidateOptionsResult Validate(string? name, AcsOptions options)
    {
        var failures = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.ConnectionString) &&
            string.IsNullOrWhiteSpace(options.SenderAddress))
        {
            failures.Add("Acs:SenderAddress is required when Acs:ConnectionString is set (email dispatch needs a From address).");
        }
        if (!string.IsNullOrWhiteSpace(options.SenderAddress) &&
            !options.SenderAddress.Contains('@', StringComparison.Ordinal))
        {
            failures.Add($"Acs:SenderAddress must be a valid email address (was '{options.SenderAddress}').");
        }
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>Blob: when an account URL is set it must be an absolute URL.</summary>
internal sealed class BlobOptionsValidator : IValidateOptions<BlobOptions>
{
    public ValidateOptionsResult Validate(string? name, BlobOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.AccountUrl) &&
            !Uri.TryCreate(options.AccountUrl, UriKind.Absolute, out _))
        {
            return ValidateOptionsResult.Fail(
                $"Blob:AccountUrl must be an absolute URL when set (was '{options.AccountUrl}').");
        }
        return ValidateOptionsResult.Success;
    }
}

/// <summary>VRB-101 — image-upload policy must allow at least one MIME type.</summary>
internal sealed class CatalogImageOptionsValidator : IValidateOptions<CatalogImageOptions>
{
    public ValidateOptionsResult Validate(string? name, CatalogImageOptions options)
    {
        if (options.AllowedContentTypes is null || options.AllowedContentTypes.Length == 0)
        {
            return ValidateOptionsResult.Fail(
                "Catalog:Images:AllowedContentTypes must list at least one MIME type.");
        }
        return ValidateOptionsResult.Success;
    }
}
