using PjSip.Net.Calls;

namespace PjSip.Net.Events;

public sealed class CallStateChangedEventArgs : SipEventArgs
{
    public required ISipCall Call { get; init; }
    public required SipCallState OldState { get; init; }
    public required SipCallState NewState { get; init; }
}
