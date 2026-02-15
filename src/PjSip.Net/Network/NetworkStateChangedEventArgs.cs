using PjSip.Net.Events;

namespace PjSip.Net.Network;

public sealed class NetworkStateChangedEventArgs : SipEventArgs
{
    public required NetworkState OldState { get; init; }
    public required NetworkState NewState { get; init; }
}
