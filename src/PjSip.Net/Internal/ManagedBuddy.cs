using Microsoft.Extensions.Logging;
using PjSip.Net.Events;
using PjSip.Net.Interop.Generated;
using PjSip.Net.Presence;
using Gen = PjSip.Net.Interop.Generated;
using BuddyInfo = PjSip.Net.Presence.BuddyInfo;

namespace PjSip.Net.Internal;

/// <summary>
/// Managed-side buddy that bridges pjsua2 Buddy callbacks to .NET events.
/// Uses an internal <see cref="NativeBuddyBridge"/> (SWIG Director) when
/// native binaries are available; otherwise operates in stub mode for tests.
/// </summary>
internal sealed class ManagedBuddy : ISipBuddy
{
    private readonly PjSipEndpointManager _endpointManager;
    private readonly ILogger _logger;
    private NativeBuddyBridge? _native;
    private ManagedAccount? _account;
    private volatile bool _disposed;

    public ManagedBuddy(
        string uri,
        PjSipEndpointManager endpointManager,
        ILogger logger)
    {
        Uri = uri;
        _endpointManager = endpointManager;
        _logger = logger;
        Info = new BuddyInfo
        {
            Uri = uri,
            State = BuddyState.Unknown,
            LastUpdated = DateTime.UtcNow
        };
    }

    public string Uri { get; }
    public BuddyState State { get; private set; } = BuddyState.Unknown;
    public BuddyInfo Info { get; private set; }

    public event EventHandler<BuddyStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Sets the account reference for creating the native buddy subscription.
    /// </summary>
    internal void SetAccount(ManagedAccount account) => _account = account;

    public async Task SubscribeAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!NativeAvailability.IsAvailable || _account?.Native is null)
        {
            _logger.LogInformation("Subscribing to presence for {Uri} (stub mode)", Uri);
            return;
        }

        await _endpointManager.Invoker.InvokeAsync(() =>
        {
            _logger.LogInformation("Subscribing to presence for {Uri}", Uri);

            if (_native is null)
            {
                _native = new NativeBuddyBridge(this, _logger);

                using var cfg = new BuddyConfig();
                cfg.uri = Uri;
                cfg.subscribe = true;

                _native.create(_account.Native!, cfg);
            }

            _native.subscribePresence(true);
        });
    }

    public async Task UnsubscribeAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_native is null)
        {
            _logger.LogInformation("Unsubscribing from presence for {Uri} (stub mode)", Uri);
            return;
        }

        await _endpointManager.Invoker.InvokeAsync(() =>
        {
            _logger.LogInformation("Unsubscribing from presence for {Uri}", Uri);
            _native.subscribePresence(false);
        });
    }

    internal void SetState(BuddyState newState, string? statusText = null)
    {
        var oldState = State;
        var oldStatusText = Info.StatusText;

        // Skip if nothing changed (state AND statusText both identical)
        if (oldState == newState && oldStatusText == statusText) return;

        State = newState;

        Info = Info with
        {
            State = newState,
            StatusText = statusText,
            LastUpdated = DateTime.UtcNow
        };

        StateChanged?.Invoke(this, new BuddyStateChangedEventArgs
        {
            Buddy = this,
            OldState = oldState,
            NewState = newState,
            StatusText = statusText
        });
    }

    /// <summary>
    /// Called from the native bridge when buddy state changes.
    /// </summary>
    internal void OnNativeBuddyState()
    {
        if (_native is null) return;

        try
        {
            var info = _native.getInfo();
            var status = info.presStatus.status;
            _logger.LogDebug("Buddy {Uri} native state: status={Status} activity={Activity} statusText={Text}",
                Uri, status, info.presStatus.activity, info.presStatus.statusText);

            var newState = status switch
            {
                pjsua_buddy_status.PJSUA_BUDDY_STATUS_ONLINE => BuddyState.Online,
                pjsua_buddy_status.PJSUA_BUDDY_STATUS_OFFLINE => BuddyState.Offline,
                _ => BuddyState.Unknown
            };

            // Refine based on activity
            if (newState == BuddyState.Online)
            {
                newState = info.presStatus.activity switch
                {
                    pjrpid_activity.PJRPID_ACTIVITY_AWAY => BuddyState.Away,
                    pjrpid_activity.PJRPID_ACTIVITY_BUSY => BuddyState.Busy,
                    _ => BuddyState.Online
                };
            }

            // BLF hint: parse statusText for Ring/Talk indicators (common in Asterisk/FusionPBX)
            var statusText = info.presStatus.statusText;
            if (newState == BuddyState.Online && !string.IsNullOrEmpty(statusText))
            {
                if (statusText.StartsWith("Ring", StringComparison.OrdinalIgnoreCase) ||
                    statusText.StartsWith("Talk", StringComparison.OrdinalIgnoreCase))
                {
                    newState = BuddyState.OnThePhone;
                }
            }

            _logger.LogDebug("Buddy {Uri} mapped state: {OldState} -> {NewState}", Uri, State, newState);
            SetState(newState, info.presStatus.statusText);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading buddy state for {Uri}", Uri);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_native is not null && State is not BuddyState.Offline and not BuddyState.Unknown)
        {
            // Unsubscribe synchronously on the PJSIP thread to avoid deadlock
            try
            {
                _endpointManager.Invoker.Invoke(() =>
                {
                    try { _native.subscribePresence(false); }
                    catch (Exception ex) { _logger.LogDebug(ex, "Best-effort unsubscribe during dispose of buddy {Uri}", Uri); }
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error during buddy dispose for {Uri}", Uri);
            }
        }

        _native?.Dispose();
        _native = null;
    }

    /// <summary>
    /// SWIG Director subclass of <see cref="Gen.Buddy"/> that receives
    /// native pjsua2 callbacks and delegates them to the parent ManagedBuddy.
    /// </summary>
    private sealed class NativeBuddyBridge : Gen.Buddy
    {
        private readonly ManagedBuddy _managed;
        private readonly ILogger _logger;

        public NativeBuddyBridge(ManagedBuddy managed, ILogger logger) : base()
        {
            _managed = managed;
            _logger = logger;
        }

        public override void onBuddyState()
        {
            try
            {
                _logger.LogDebug("onBuddyState callback for {Uri}", _managed.Uri);
                _managed.OnNativeBuddyState();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in onBuddyState callback");
            }
        }

        public override void onBuddyEvSubState(OnBuddyEvSubStateParam prm)
        {
            try
            {
                _logger.LogDebug("onBuddyEvSubState callback for {Uri}", _managed.Uri);
                _managed.OnNativeBuddyState();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in onBuddyEvSubState callback");
            }
        }
    }
}
