using PjSip.Net.Accounts;
using PjSip.Net.Calls;

namespace PjSip.Net.Events;

public sealed class IncomingCallEventArgs : SipEventArgs
{
    public required ISipCall Call { get; init; }
    public required ISipAccount Account { get; init; }
    public required string RemoteUri { get; init; }
    public string? RemoteDisplayName { get; init; }
}
