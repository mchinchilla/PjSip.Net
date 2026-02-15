using PjSip.Net.Accounts;
using PjSip.Net.Messaging;

namespace PjSip.Net.Events;

public sealed class MwiStateChangedEventArgs : SipEventArgs
{
    public required ISipAccount Account { get; init; }
    public required MwiInfo Info { get; init; }
}
