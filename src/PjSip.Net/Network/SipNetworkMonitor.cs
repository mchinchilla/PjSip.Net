using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using PjSip.Net.Internal;
using PjSip.Net.Interop.Generated;

namespace PjSip.Net.Network;

internal sealed class SipNetworkMonitor : ISipNetworkMonitor
{
    private readonly PjSipEndpointManager _endpointManager;
    private readonly ILogger _logger;
    private volatile bool _monitoring;
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

    internal void StartMonitoring()
    {
        if (_monitoring) return;
        _monitoring = true;

        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

        _logger.LogInformation("Network monitoring started");
    }

    public async Task HandleNetworkChangeAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var oldState = CurrentState;
        CurrentState = NetworkState.Changed;

        var ep = _endpointManager.Endpoint;
        if (ep is not null)
        {
            await _endpointManager.Invoker.InvokeAsync(() =>
            {
                _logger.LogInformation("Handling network change, re-registering transports and accounts");

                using var ipChangeParam = new IpChangeParam();
                ipChangeParam.restartListener = true;
                ipChangeParam.restartLisDelay = 100;
                ep.handleIpChange(ipChangeParam);
            });
        }
        else
        {
            _logger.LogInformation("Handling network change (stub mode)");
        }

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

    private async void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (_disposed) return;

        if (e.IsAvailable)
        {
            _logger.LogInformation("Network became available, triggering IP change handling");
            try
            {
                await HandleNetworkChangeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling network availability change");
            }
        }
        else
        {
            _logger.LogWarning("Network became unavailable");
            OnNetworkDisconnected();
        }
    }

    private async void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;

        _logger.LogInformation("Network address changed, triggering IP change handling");
        try
        {
            await HandleNetworkChangeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling network address change");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _monitoring = false;

        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
    }
}
