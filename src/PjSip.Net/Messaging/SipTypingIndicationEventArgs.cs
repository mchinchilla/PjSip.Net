using PjSip.Net.Events;

namespace PjSip.Net.Messaging;

public sealed class SipTypingIndicationEventArgs : SipEventArgs
{
    public required string FromUri { get; init; }
    public required bool IsTyping { get; init; }
}
