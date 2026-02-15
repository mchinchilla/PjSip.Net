using Microsoft.Extensions.Logging;
using PjSip.Net.Events;
using PjSip.Net.Internal;
using PjSip.Net.Interop.Generated;

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
    private readonly List<ManagedAccount> _accounts = [];
    private ManagedAccount? _primaryAccount;

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

    /// <summary>
    /// Adds an account for presence operations. The first account added is the primary.
    /// </summary>
    internal void AddAccount(ManagedAccount account)
    {
        lock (_lock) _accounts.Add(account);
        _primaryAccount ??= account;
    }

    /// <summary>
    /// Removes an account from presence operations.
    /// </summary>
    internal void RemoveAccount(ManagedAccount account)
    {
        lock (_lock) _accounts.Remove(account);
        if (_primaryAccount == account)
            _primaryAccount = _accounts.FirstOrDefault();
    }

    public ISipBuddy AddBuddy(string uri)
    {
        var buddy = new ManagedBuddy(uri, _endpointManager, _logger);
        if (_primaryAccount is not null)
            buddy.SetAccount(_primaryAccount);

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

        ManagedAccount[] accounts;
        lock (_lock) accounts = [.. _accounts];

        if (accounts.Length == 0 || accounts.All(a => a.Native is null))
        {
            _logger.LogInformation("Setting my presence to {State} ({StatusText}) (stub mode)", state, statusText);
            return;
        }

        await _endpointManager.Invoker.InvokeAsync(() =>
        {
            _logger.LogInformation("Setting my presence to {State} ({StatusText})", state, statusText);

            using var presStatus = new PresenceStatus();
            presStatus.status = state switch
            {
                BuddyState.Online => pjsua_buddy_status.PJSUA_BUDDY_STATUS_ONLINE,
                BuddyState.Offline => pjsua_buddy_status.PJSUA_BUDDY_STATUS_OFFLINE,
                _ => pjsua_buddy_status.PJSUA_BUDDY_STATUS_ONLINE
            };

            presStatus.activity = state switch
            {
                BuddyState.Away => pjrpid_activity.PJRPID_ACTIVITY_AWAY,
                BuddyState.Busy or BuddyState.OnThePhone => pjrpid_activity.PJRPID_ACTIVITY_BUSY,
                _ => pjrpid_activity.PJRPID_ACTIVITY_UNKNOWN
            };

            if (statusText is not null)
                presStatus.statusText = statusText;

            // Apply presence to all accounts
            foreach (var account in accounts)
            {
                try { account.Native?.setOnlineStatus(presStatus); }
                catch (Exception ex) { _logger.LogWarning(ex, "Error setting presence on account {Uri}", account.Uri); }
            }
        });
    }

    private void OnBuddyStateChanged(object? sender, BuddyStateChangedEventArgs e)
    {
        BuddyStateChanged?.Invoke(this, e);
    }
}
