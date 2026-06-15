using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Interfaces;

namespace VrBook.Modules.Notifications.Infrastructure.Email;

/// <summary>
/// Slice 4 C2: <see cref="IEmailSender"/> over Azure Communication Services
/// Email (per ADR-0011). Ports the LankaConnect <c>AzureEmailService</c>
/// retry pattern: on a 429 / rate-limit response, retries up to 3 times
/// with exponential backoff (2s / 4s / 8s).
/// </summary>
internal sealed class AzureEmailSender : IEmailSender
{
    private const int MaxAttempts = 3;
    private const int BaseDelayMs = 2_000;

    private readonly EmailClient _client;
    private readonly string _senderAddress;
    private readonly ILogger<AzureEmailSender> _logger;

    public AzureEmailSender(IConfiguration configuration, ILogger<AzureEmailSender> logger)
    {
        _logger = logger;
        var connection = configuration["Acs:ConnectionString"]
            ?? throw new InvalidOperationException(
                "Acs:ConnectionString is not configured. Required for AzureEmailSender.");
        _senderAddress = configuration["Acs:SenderAddress"]
            ?? throw new InvalidOperationException(
                "Acs:SenderAddress is not configured. Required for AzureEmailSender.");
        _client = new EmailClient(connection);
    }

    public async Task<EmailDispatchResult> SendAsync(EmailDispatchRequest request, CancellationToken cancellationToken = default)
    {
        var content = new EmailContent(request.Subject)
        {
            Html = string.IsNullOrWhiteSpace(request.HtmlBody) ? null : request.HtmlBody,
            PlainText = string.IsNullOrWhiteSpace(request.PlainTextBody) ? null : request.PlainTextBody,
        };
        var recipients = new EmailRecipients(new[]
        {
            new EmailAddress(request.ToEmail, request.ToDisplayName),
        });
        var message = new EmailMessage(_senderAddress, recipients, content);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var operation = await _client.SendAsync(WaitUntil.Completed, message, cancellationToken);
                var status = operation.Value.Status;
                if (status == EmailSendStatus.Succeeded)
                {
                    _logger.LogInformation(
                        "ACS dispatched email to {ToEmail}; operationId={OperationId}.",
                        request.ToEmail, operation.Id);
                    return EmailDispatchResult.Success(operation.Id);
                }
                _logger.LogWarning(
                    "ACS reported non-Success status {Status} for {ToEmail}; operationId={OperationId}.",
                    status, request.ToEmail, operation.Id);
                return EmailDispatchResult.Failure($"ACS status: {status}");
            }
            catch (RequestFailedException ex) when (ex.Status == 429 && attempt < MaxAttempts)
            {
                var delayMs = BaseDelayMs * (int)Math.Pow(2, attempt - 1);
                _logger.LogWarning(ex,
                    "ACS 429 for {ToEmail} attempt {Attempt}/{Max}; backing off {DelayMs}ms.",
                    request.ToEmail, attempt, MaxAttempts, delayMs);
                await Task.Delay(delayMs, cancellationToken);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex,
                    "ACS RequestFailedException for {ToEmail}; status={Status} errorCode={ErrorCode}.",
                    request.ToEmail, ex.Status, ex.ErrorCode);
                return EmailDispatchResult.Failure($"ACS {ex.Status}: {ex.ErrorCode ?? ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error dispatching email to {ToEmail}.", request.ToEmail);
                return EmailDispatchResult.Failure(ex.Message);
            }
        }

        return EmailDispatchResult.Failure("ACS rate-limited; retry budget exhausted.");
    }
}
