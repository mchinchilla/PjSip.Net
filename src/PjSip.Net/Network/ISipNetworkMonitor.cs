namespace PjSip.Net.Network;

public interface ISipNetworkMonitor : IDisposable
{
    NetworkState CurrentState { get; }
    event EventHandler<NetworkStateChangedEventArgs>? NetworkStateChanged;
    Task HandleNetworkChangeAsync(CancellationToken ct = default);
}
