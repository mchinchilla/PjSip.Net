using PjSip.Net.Events;

namespace PjSip.Net.Messaging;

public sealed class SipMessageReceivedEventArgs : SipEventArgs
{
    public required SipMessage Message { get; init; }
}
