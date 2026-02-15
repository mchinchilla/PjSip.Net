using PjSip.Net.Events;

namespace PjSip.Net.Messaging;

public sealed class SipMessageStatusEventArgs : SipEventArgs
{
    public required string DestinationUri { get; init; }
    public required int StatusCode { get; init; }
    public string? Reason { get; init; }
}
