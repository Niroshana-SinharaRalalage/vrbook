using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VrBook.Modules.Sync.Infrastructure.RateLimiting;
using Xunit;

namespace VrBook.Api.IntegrationTests.Sync;

/// <summary>
/// Slice OPS.M.6 §3.4 (D4) — pins the DelegatingHandler's gate behavior:
/// inner handler runs only when a token is acquired; otherwise a synthetic
/// 429 short-circuits.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OutboundRateLimitHandlerTests
{
    [Fact]
    public async Task Inner_handler_is_called_when_token_acquired()
    {
        var limiter = Substitute.For<IRateLimiter>();
        limiter.TryAcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<bool>(true));

        var (handler, inner) = NewHandler(limiter);
        await Send(handler, "https://www.airbnb.com/calendar/feed");

        inner.Invocations.Should().Be(1);
    }

    [Fact]
    public async Task Synthetic_429_returned_when_token_not_acquired()
    {
        var limiter = Substitute.For<IRateLimiter>();
        limiter.TryAcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<bool>(false));

        var (handler, inner) = NewHandler(limiter);
        var resp = await Send(handler, "https://www.airbnb.com/calendar/feed");

        resp.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        inner.Invocations.Should().Be(0,
            because: "rate-limit denial short-circuits before the inner handler is invoked.");
    }

    [Fact]
    public async Task Host_is_extracted_from_request_uri_for_limiter_key()
    {
        string? observedHost = null;
        var limiter = Substitute.For<IRateLimiter>();
        limiter.TryAcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => { observedHost = call.Arg<string>(); return new ValueTask<bool>(true); });

        var (handler, _) = NewHandler(limiter);
        await Send(handler, "https://de.airbnb.com/calendar/abc.ics");

        observedHost.Should().Be("de.airbnb.com");
    }

    private static (OutboundRateLimitHandler handler, RecordingInner inner) NewHandler(IRateLimiter limiter)
    {
        var inner = new RecordingInner();
        var handler = new OutboundRateLimitHandler(
            limiter,
            NullLogger<OutboundRateLimitHandler>.Instance)
        {
            InnerHandler = inner,
        };
        return (handler, inner);
    }

    private static Task<HttpResponseMessage> Send(OutboundRateLimitHandler handler, string url)
    {
        var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
        return invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, url), CancellationToken.None);
    }

    private sealed class RecordingInner : HttpMessageHandler
    {
        public int Invocations { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Invocations++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
