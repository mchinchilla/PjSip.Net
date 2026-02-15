using Microsoft.Extensions.Logging;
using PjSip.Net.Accounts;
using PjSip.Net.Calls;
using PjSip.Net.Events;
using PjSip.Net.Messaging;

namespace PjSip.Net.Internal;

/// <summary>
/// Internal adapter that will subclass pj.Account to bridge pjsua2 callbacks
/// to .NET events. Until SWIG-generated classes are available, this serves
/// as the managed-side account state holder.
/// </summary>
internal sealed class ManagedAccount : ISipAccount
{
    private readonly PjSipEndpointManager _endpointManager;
    private readonly ILogger _logger;
    private readonly List<ManagedCall> _activeCalls = [];
    private readonly object _lock = new();
    private volatile bool _disposed;

    public ManagedAccount(
        SipAccountOptions options,
        PjSipEndpointManager endpointManager,
        ILogger logger)
    {
        Options = options;
        _endpointManager = endpointManager;
        _logger = logger;
        Id = Guid.NewGuid().ToString("N")[..8];
        Uri = $"sip:{options.Username}@{options.Domain}";
    }

    public string Id { get; }
    public string Uri { get; }
    public SipRegistrationState RegistrationState { get; private set; } = SipRegistrationState.Unregistered;
    public SipAccountOptions Options { get; }
    public IReadOnlyList<ISipCall> ActiveCalls
    {
        get { lock (_lock) return _activeCalls.ToArray(); }
    }

    public DndMode DndMode { get; set; } = DndMode.Off;
    public CallForwardingOptions CallForwarding { get; } = new();
    public MwiInfo? MwiInfo { get; private set; }

    public event EventHandler<RegistrationStateChangedEventArgs>? RegistrationStateChanged;
    public event EventHandler<IncomingCallEventArgs>? IncomingCall;
    public event EventHandler<MwiStateChangedEventArgs>? MwiStateChanged;

    public async Task RegisterAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var oldState = RegistrationState;
        RegistrationState = SipRegistrationState.Registering;

        await _endpointManager.Invoker.InvokeAsync(() =>
        {
            _logger.LogInformation("Registering account {Uri}", Uri);

            // TODO: When SWIG-generated classes are available:
            // 1. Create pj.AccountConfig from Options
            // 2. Call nativeAccount.create(epConfig)
            // 3. Wait for onRegState callback
        });

        OnRegistrationStateChanged(oldState, RegistrationState);
    }

    public async Task UnregisterAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var oldState = RegistrationState;
        RegistrationState = SipRegistrationState.Unregistering;

        await _endpointManager.Invoker.InvokeAsync(() =>
        {
            _logger.LogInformation("Unregistering account {Uri}", Uri);

            // TODO: When SWIG-generated classes are available:
            // 1. Call nativeAccount.setRegistration(false)
        });

        RegistrationState = SipRegistrationState.Unregistered;
        OnRegistrationStateChanged(oldState, RegistrationState);
    }

    public ISipCall MakeCall(string destinationUri)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var call = new ManagedCall(this, destinationUri, CallDirection.Outgoing, _endpointManager, _logger);
        lock (_lock) _activeCalls.Add(call);
        call.StateChanged += OnCallStateChanged;
        return call;
    }

    public ISipCall MakeCall(string destinationUri, IEnumerable<SipHeader> headers)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var call = new ManagedCall(this, destinationUri, CallDirection.Outgoing, _endpointManager, _logger, headers);
        lock (_lock) _activeCalls.Add(call);
        call.StateChanged += OnCallStateChanged;
        return call;
    }

    internal void OnIncomingCall(ManagedCall call)
    {
        // Check DND mode
        if (DndMode == DndMode.RejectAll)
        {
            _logger.LogInformation("Rejecting incoming call due to DND mode (RejectAll)");
            call.Hangup(486);
            return;
        }
        if (DndMode == DndMode.RejectWithBusy)
        {
            _logger.LogInformation("Rejecting incoming call with busy due to DND mode");
            call.Hangup(486);
            return;
        }

        // Check call forwarding
        if (CallForwarding.Enabled && CallForwarding.Type == CallForwardingType.Unconditional && CallForwarding.DestinationUri is not null)
        {
            _logger.LogInformation("Forwarding incoming call to {Destination}", CallForwarding.DestinationUri);
            call.Transfer(CallForwarding.DestinationUri);
            return;
        }

        lock (_lock) _activeCalls.Add(call);
        call.StateChanged += OnCallStateChanged;
        IncomingCall?.Invoke(this, new IncomingCallEventArgs
        {
            Call = call,
            Account = this,
            RemoteUri = call.Info.RemoteUri,
            RemoteDisplayName = call.Info.RemoteDisplayName
        });
    }

    internal void OnMwiInfo(MwiInfo mwiInfo)
    {
        MwiInfo = mwiInfo;
        MwiStateChanged?.Invoke(this, new MwiStateChangedEventArgs
        {
            Account = this,
            Info = mwiInfo
        });
    }

    private void OnCallStateChanged(object? sender, CallStateChangedEventArgs e)
    {
        if (e.NewState == SipCallState.Disconnected && sender is ManagedCall call)
        {
            lock (_lock) _activeCalls.Remove(call);
            call.StateChanged -= OnCallStateChanged;
        }
    }

    private void OnRegistrationStateChanged(SipRegistrationState oldState, SipRegistrationState newState)
    {
        RegistrationStateChanged?.Invoke(this, new RegistrationStateChangedEventArgs
        {
            Account = this,
            OldState = oldState,
            NewState = newState
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_lock)
        {
            foreach (var call in _activeCalls.ToArray())
            {
                call.Dispose();
            }
            _activeCalls.Clear();
        }
    }
}
