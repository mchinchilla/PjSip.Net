using PjSip.Net.Calls;
using PjSip.Net.Events;
using PjSip.Net.Messaging;

namespace PjSip.Net.Accounts;

public interface ISipAccount : IDisposable
{
    string Id { get; }
    string Uri { get; }
    SipRegistrationState RegistrationState { get; }
    SipAccountOptions Options { get; }
    IReadOnlyList<ISipCall> ActiveCalls { get; }
    DndMode DndMode { get; set; }
    CallForwardingOptions CallForwarding { get; }
    MwiInfo? MwiInfo { get; }

    event EventHandler<RegistrationStateChangedEventArgs>? RegistrationStateChanged;
    event EventHandler<IncomingCallEventArgs>? IncomingCall;
    event EventHandler<MwiStateChangedEventArgs>? MwiStateChanged;

    Task RegisterAsync(CancellationToken cancellationToken = default);
    Task UnregisterAsync(CancellationToken cancellationToken = default);
    ISipCall MakeCall(string destinationUri);
    ISipCall MakeCall(string destinationUri, IEnumerable<SipHeader> headers);
}
