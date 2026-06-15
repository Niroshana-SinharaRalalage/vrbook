namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Slice 4 C2: pre-rendered email dispatch. The Notifications dispatcher
/// renders the Mustache template (C3) into an <see cref="EmailDispatchRequest"/>
/// then hands it off here. Implementations: <c>AzureEmailSender</c> (ACS, Phase 1+),
/// future SMTP fallback, in-memory fake for tests.
///
/// <para>
/// The sender owns transient-failure retry semantics (e.g. ACS 429
/// rate-limit retry with 2s/4s/8s backoff per ADR-0011) so callers see a
/// single result. Permanent failures bubble as <see cref="EmailDispatchResult.Failure"/>
/// and the worker's outer loop records the failure on the
/// <c>NotificationLog</c> row.
/// </para>
/// </summary>
public interface IEmailSender
{
    Task<EmailDispatchResult> SendAsync(EmailDispatchRequest request, CancellationToken cancellationToken = default);
}

public sealed record EmailDispatchRequest(
    string ToEmail,
    string ToDisplayName,
    string Subject,
    string HtmlBody,
    string PlainTextBody);

public sealed record EmailDispatchResult(bool IsSuccess, string? Error, string? ProviderMessageId)
{
    public static EmailDispatchResult Success(string? providerMessageId = null) =>
        new(true, null, providerMessageId);

    public static EmailDispatchResult Failure(string error) =>
        new(false, error, null);
}
