using Microsoft.Extensions.Logging;
using PjSip.Net.Internal;

namespace PjSip.Net.Network;

internal sealed class SipNetworkMonitor : ISipNetworkMonitor
{
    private readonly PjSipEndpointManager _endpointManager;
    private readonly ILogger _logger;
    private volatile bool _disposed;

    public SipNetworkMonitor(
        PjSipEndpointManager endpointManager,
        ILogger logger)
    {
        _endpointManager = endpointManager;
        _logger = logger;
    }

    public NetworkState CurrentState { get; private set; } = NetworkState.Connected;

    public event EventHandler<NetworkStateChangedEventArgs>? NetworkStateChanged;

    public async Task HandleNetworkChangeAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var oldState = CurrentState;
        CurrentState = NetworkState.Changed;

        await _endpointManager.Invoker.InvokeAsync(() =>
        {
            _logger.LogInformation("Handling network change, re-registering transports and accounts");

            // TODO: When SWIG-generated classes are available:
            // 1. Call ep.handleIpChange(IpChangeParam)
            // 2. This triggers transport restart and re-registration
        });

        CurrentState = NetworkState.Connected;
        NetworkStateChanged?.Invoke(this, new NetworkStateChangedEventArgs
        {
            OldState = oldState,
            NewState = NetworkState.Connected
        });
    }

    internal void OnNetworkDisconnected()
    {
        var oldState = CurrentState;
        CurrentState = NetworkState.Disconnected;
        NetworkStateChanged?.Invoke(this, new NetworkStateChangedEventArgs
        {
            OldState = oldState,
            NewState = NetworkState.Disconnected
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
