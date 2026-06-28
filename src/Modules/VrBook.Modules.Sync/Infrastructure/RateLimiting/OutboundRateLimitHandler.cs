using System.Net;
using Microsoft.Extensions.Logging;

namespace VrBook.Modules.Sync.Infrastructure.RateLimiting;

/// <summary>
/// OPS.M.6 §3.4 (D4) — <see cref="DelegatingHandler"/> attached to the
/// <c>AirBnBICal</c> named client. Before sending a request, asks the
/// <see cref="IRateLimiter"/> for a token keyed on the request's host.
/// On denial, short-circuits with a synthetic 429 so the upstream caller's
/// <c>EnsureSuccessStatusCode</c> bubbles into the worker's per-feed catch.
/// </summary>
public sealed class OutboundRateLimitHandler(
    IRateLimiter limiter,
    ILogger<OutboundRateLimitHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var host = request.RequestUri?.Host
            ?? throw new InvalidOperationException("Outbound request must have a RequestUri with a Host.");

        var acquired = await limiter.TryAcquireAsync(host, cancellationToken);
        if (!acquired)
        {
            logger.LogWarning(
                "Outbound rate-limit denied request host={Host}; returning synthetic 429.", host);
            return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                ReasonPhrase = "Outbound rate-limit (vrbook)",
                RequestMessage = request,
            };
        }
        return await base.SendAsync(request, cancellationToken);
    }
}
