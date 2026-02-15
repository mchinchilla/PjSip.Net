using PjSip.Net.Calls;
using PjSip.Net.Events;
using PjSip.Net.Internal;
using PjSip.Net.Messaging;

namespace PjSip.Net.Accounts;

/// <summary>
/// Public-facing SipAccount that delegates to the internal ManagedAccount.
/// This thin wrapper exists to keep internal PJSIP types out of the public API.
/// </summary>
internal sealed class SipAccount : ISipAccount
{
    private readonly ManagedAccount _inner;

    internal SipAccount(ManagedAccount inner)
    {
        _inner = inner;
        _inner.RegistrationStateChanged += (s, e) => RegistrationStateChanged?.Invoke(this, e);
        _inner.IncomingCall += (s, e) => IncomingCall?.Invoke(this, e);
        _inner.MwiStateChanged += (s, e) => MwiStateChanged?.Invoke(this, e);
    }

    internal ManagedAccount Inner => _inner;

    public string Id => _inner.Id;
    public string Uri => _inner.Uri;
    public SipRegistrationState RegistrationState => _inner.RegistrationState;
    public SipAccountOptions Options => _inner.Options;
    public IReadOnlyList<ISipCall> ActiveCalls => _inner.ActiveCalls;
    public DndMode DndMode { get => _inner.DndMode; set => _inner.DndMode = value; }
    public CallForwardingOptions CallForwarding => _inner.CallForwarding;
    public MwiInfo? MwiInfo => _inner.MwiInfo;

    public event EventHandler<RegistrationStateChangedEventArgs>? RegistrationStateChanged;
    public event EventHandler<IncomingCallEventArgs>? IncomingCall;
    public event EventHandler<MwiStateChangedEventArgs>? MwiStateChanged;

    public Task RegisterAsync(CancellationToken cancellationToken = default) =>
        _inner.RegisterAsync(cancellationToken);

    public Task UnregisterAsync(CancellationToken cancellationToken = default) =>
        _inner.UnregisterAsync(cancellationToken);

    public ISipCall MakeCall(string destinationUri) =>
        _inner.MakeCall(destinationUri);

    public ISipCall MakeCall(string destinationUri, IEnumerable<SipHeader> headers) =>
        _inner.MakeCall(destinationUri, headers);

    public void Dispose() => _inner.Dispose();
}
