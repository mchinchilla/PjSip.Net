using Microsoft.Extensions.Logging;
using PjSip.Net.Events;
using PjSip.Net.Presence;

namespace PjSip.Net.Internal;

/// <summary>
/// Internal adapter that will subclass pj.Buddy to bridge pjsua2 callbacks
/// to .NET events. Until SWIG-generated classes are available, this serves
/// as the managed-side buddy/presence state holder.
/// </summary>
internal sealed class ManagedBuddy : ISipBuddy
{
    private readonly PjSipEndpointManager _endpointManager;
    private readonly ILogger _logger;
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

    public async Task SubscribeAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _endpointManager.Invoker.InvokeAsync(() =>
        {
            _logger.LogInformation("Subscribing to presence for {Uri}", Uri);

            // TODO: When SWIG-generated classes are available:
            // 1. Create pj.BuddyConfig with the URI
            // 2. Call nativeBuddy.create(account, buddyConfig)
            // 3. Call nativeBuddy.subscribePresence(true)
        });
    }

    public async Task UnsubscribeAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _endpointManager.Invoker.InvokeAsync(() =>
        {
            _logger.LogInformation("Unsubscribing from presence for {Uri}", Uri);

            // TODO: When SWIG-generated classes are available:
            // 1. Call nativeBuddy.subscribePresence(false)
        });
    }

    internal void SetState(BuddyState newState, string? statusText = null)
    {
        var oldState = State;
        if (oldState == newState) return;
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (State is not BuddyState.Offline and not BuddyState.Unknown)
        {
            try { UnsubscribeAsync().GetAwaiter().GetResult(); } catch { /* best effort */ }
        }
    }
}
