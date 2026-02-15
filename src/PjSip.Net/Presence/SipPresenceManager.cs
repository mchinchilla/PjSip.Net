using Microsoft.Extensions.Logging;
using PjSip.Net.Events;
using PjSip.Net.Internal;

namespace PjSip.Net.Presence;

/// <summary>
/// Manages buddy/presence subscriptions and publishes the local user's
/// presence state via SIP SUBSCRIBE/NOTIFY (BLF / Busy Lamp Field).
/// </summary>
internal sealed class SipPresenceManager : ISipPresenceManager
{
    private readonly PjSipEndpointManager _endpointManager;
    private readonly ILogger _logger;
    private readonly List<ManagedBuddy> _buddies = [];
    private readonly object _lock = new();

    public SipPresenceManager(
        PjSipEndpointManager endpointManager,
        ILogger logger)
    {
        _endpointManager = endpointManager;
        _logger = logger;
    }

    public BuddyState MyState { get; private set; } = BuddyState.Online;

    public IReadOnlyList<ISipBuddy> Buddies
    {
        get { lock (_lock) return _buddies.ToArray(); }
    }

    public event EventHandler<BuddyStateChangedEventArgs>? BuddyStateChanged;

    public ISipBuddy AddBuddy(string uri)
    {
        var buddy = new ManagedBuddy(uri, _endpointManager, _logger);
        lock (_lock) _buddies.Add(buddy);
        buddy.StateChanged += OnBuddyStateChanged;

        _logger.LogInformation("Added buddy {Uri}, starting presence subscription", uri);

        // Fire-and-forget subscribe; errors are logged inside SubscribeAsync
        _ = buddy.SubscribeAsync();

        return buddy;
    }

    public void RemoveBuddy(ISipBuddy buddy)
    {
        if (buddy is not ManagedBuddy managed) return;

        managed.StateChanged -= OnBuddyStateChanged;
        lock (_lock) _buddies.Remove(managed);
        managed.Dispose();

        _logger.LogInformation("Removed buddy {Uri}", buddy.Uri);
    }

    public async Task SetMyPresenceAsync(BuddyState state, string? statusText = null, CancellationToken ct = default)
    {
        MyState = state;

        await _endpointManager.Invoker.InvokeAsync(() =>
        {
            _logger.LogInformation("Setting my presence to {State} ({StatusText})", state, statusText);

            // TODO: When SWIG-generated classes are available:
            // 1. Build pj.PresenceStatus from state + statusText
            // 2. Call account.setOnlineStatus(presenceStatus)
        });
    }

    private void OnBuddyStateChanged(object? sender, BuddyStateChangedEventArgs e)
    {
        BuddyStateChanged?.Invoke(this, e);
    }
}
