using PjSip.Net.Calls;

namespace PjSip.Net.Events;

public sealed class CallMediaStateChangedEventArgs : SipEventArgs
{
    public required ISipCall Call { get; init; }
    public required bool IsActive { get; init; }
}
