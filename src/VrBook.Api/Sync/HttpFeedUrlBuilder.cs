using Microsoft.AspNetCore.Http.Extensions;
using VrBook.Modules.Sync.Application.ChannelFeeds.Commands;

namespace VrBook.Api.Sync;

/// <summary>
/// Builds the absolute URL for <c>/feeds/{token}.ics</c> using the incoming request's
/// scheme + host. Falls back to the <c>VrBook__PublicBaseUrl</c> env var when no
/// request context is available (e.g. background workers).
/// </summary>
internal sealed class HttpFeedUrlBuilder(IHttpContextAccessor accessor, IConfiguration cfg) : IFeedUrlBuilder
{
    public string OutboundFeedUrl(string outboundToken)
    {
        var path = $"/feeds/{outboundToken}.ics";
        var req = accessor.HttpContext?.Request;
        if (req is not null)
        {
            return new UriBuilder(req.Scheme, req.Host.Host, req.Host.Port ?? -1, path).Uri.ToString();
        }
        var basePath = cfg["VrBook:PublicBaseUrl"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return path; // relative — admin UI will resolve against window.location
        }
        return basePath + path;
    }
}
