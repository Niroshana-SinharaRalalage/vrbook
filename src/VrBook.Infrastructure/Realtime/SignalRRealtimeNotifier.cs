using Microsoft.Azure.SignalR.Management;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Interfaces;

namespace VrBook.Infrastructure.Realtime;

/// <summary>
/// Singleton SignalR-Service backed implementation of <see cref="IRealtimeNotifier"/>.
/// Holds a single cached <see cref="ServiceHubContext"/> via a <c>Lazy&lt;Task&lt;&gt;&gt;</c>
/// so the first call pays the construction cost once; every subsequent call reuses
/// the same hub instance. See <c>docs/SLICE7_PLAN.md</c> §2.5 + §2.6 + §2.7.
/// </summary>
internal sealed class SignalRRealtimeNotifier : IRealtimeNotifier, IAsyncDisposable
{
    private const string HubName = "notifications";
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

    private readonly ILogger<SignalRRealtimeNotifier> _logger;
    private readonly Lazy<Task<ServiceHubContext>> _hub;
    private readonly ServiceManager _manager;

    public SignalRRealtimeNotifier(string connectionString, ILogger<SignalRRealtimeNotifier> logger)
    {
        _logger = logger;
        _manager = new ServiceManagerBuilder()
            .WithOptions(o => o.ConnectionString = connectionString)
            .BuildServiceManager();
        _hub = new Lazy<Task<ServiceHubContext>>(
            () => _manager.CreateHubContextAsync(HubName, CancellationToken.None));
    }

    public async Task NotifyUserAsync(Guid userId, string method, object payload, CancellationToken ct = default)
    {
        try
        {
            var hub = await _hub.Value.ConfigureAwait(false);
            await hub.Clients.User(userId.ToString())
                .SendCoreAsync(method, new[] { payload }, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Realtime is best-effort; never throw into the caller.
            _logger.LogWarning(ex,
                "SignalR push failed for user {UserId} method {Method}",
                userId, method);
        }
    }

    public async Task<NegotiateResult> NegotiateForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var hub = await _hub.Value.ConfigureAwait(false);
        var resp = await hub.NegotiateAsync(
            new NegotiationOptions
            {
                UserId = userId.ToString(),
                TokenLifetime = TokenLifetime,
            },
            ct).ConfigureAwait(false);
        var expiresAt = DateTimeOffset.UtcNow.Add(TokenLifetime);
        return new NegotiateResult(resp.Url ?? string.Empty, resp.AccessToken ?? string.Empty, expiresAt);
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub.IsValueCreated)
        {
            try
            {
                var hub = await _hub.Value.ConfigureAwait(false);
                await hub.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SignalR hub context dispose failed");
            }
        }
        _manager.Dispose();
    }
}
