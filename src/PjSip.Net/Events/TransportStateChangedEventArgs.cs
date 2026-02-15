using PjSip.Net.Transport;

namespace PjSip.Net.Events;

public sealed class TransportStateChangedEventArgs : SipEventArgs
{
    public required SipTransportType TransportType { get; init; }
    public required string State { get; init; }
    public string? LastError { get; init; }
}
