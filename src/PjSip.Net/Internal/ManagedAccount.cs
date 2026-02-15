using Microsoft.Extensions.Logging;
using PjSip.Net.Accounts;
using PjSip.Net.Calls;
using PjSip.Net.Events;
using PjSip.Net.Interop.Generated;
using PjSip.Net.Messaging;
using Gen = PjSip.Net.Interop.Generated;
using SipHeader = PjSip.Net.Calls.SipHeader;

namespace PjSip.Net.Internal;

/// <summary>
/// Managed-side account that bridges pjsua2 Account callbacks to .NET events.
/// Uses an internal <see cref="NativeAccountBridge"/> (SWIG Director) when
/// native binaries are available; otherwise operates in stub mode for tests.
/// </summary>
internal sealed class ManagedAccount : ISipAccount
{
    private readonly PjSipEndpointManager _endpointManager;
    private readonly ILogger _logger;
    private readonly List<ManagedCall> _activeCalls = [];
    private readonly object _lock = new();
    private NativeAccountBridge? _native;
    private SipMessaging? _messaging;
    private volatile bool _disposed;

    public ManagedAccount(
        SipAccountOptions options,
        PjSipEndpointManager endpointManager,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Username, nameof(options.Username));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Password, nameof(options.Password));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Domain, nameof(options.Domain));

        if (options.RegistrationTimeout <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.RegistrationTimeout), "Registration timeout must be positive.");

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

    /// <summary>
    /// The native SWIG Account bridge, if available. Null in stub mode.
    /// </summary>
    internal NativeAccountBridge? Native => _native;

    public event EventHandler<RegistrationStateChangedEventArgs>? RegistrationStateChanged;
    public event EventHandler<IncomingCallEventArgs>? IncomingCall;
    public event EventHandler<MwiStateChangedEventArgs>? MwiStateChanged;

    /// <summary>
    /// Sets the messaging subsystem reference so that incoming message
    /// callbacks on the account can be forwarded.
    /// </summary>
    internal void SetMessaging(SipMessaging messaging) => _messaging = messaging;

    public async Task RegisterAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var oldState = RegistrationState;
        RegistrationState = SipRegistrationState.Registering;
        OnRegistrationStateChanged(oldState, RegistrationState);

        if (!NativeAvailability.IsAvailable || _endpointManager.Endpoint is null)
        {
            _logger.LogInformation("Registering account {Uri} (stub mode)", Uri);
            return;
        }

        await _endpointManager.Invoker.InvokeAsync(() =>
        {
            _logger.LogInformation("Registering account {Uri}", Uri);

            // Create the native bridge if not yet created
            _native ??= new NativeAccountBridge(this, _logger);

            using var acfg = new AccountConfig();
            acfg.idUri = Options.DisplayName is not null
                ? $"\"{Options.DisplayName}\" <sip:{Options.Username}@{Options.Domain}>"
                : $"sip:{Options.Username}@{Options.Domain}";

            // Registration config
            acfg.regConfig.registrarUri = Options.Registrar ?? $"sip:{Options.Domain}";
            acfg.regConfig.timeoutSec = (uint)Options.RegistrationTimeout;

            // Auth credentials
            var cred = new AuthCredInfo(
                "digest",
                Options.Realm ?? "*",
                Options.Username,
                0, // plain text
                Options.Password);
            acfg.sipConfig.authCreds.Add(cred);

            _native.create(acfg);
        });
    }

    public async Task UnregisterAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var oldState = RegistrationState;
        RegistrationState = SipRegistrationState.Unregistering;
        OnRegistrationStateChanged(oldState, RegistrationState);

        if (_native is null)
        {
            RegistrationState = SipRegistrationState.Unregistered;
            OnRegistrationStateChanged(SipRegistrationState.Unregistering, SipRegistrationState.Unregistered);
            return;
        }

        await _endpointManager.Invoker.InvokeAsync(() =>
        {
            _logger.LogInformation("Unregistering account {Uri}", Uri);
            _native.setRegistration(false);
        });
    }

    public ISipCall MakeCall(string destinationUri)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var call = new ManagedCall(this, destinationUri, CallDirection.Outgoing, _endpointManager, _logger);
        lock (_lock) _activeCalls.Add(call);
        call.StateChanged += OnCallStateChanged;

        if (NativeAvailability.IsAvailable && _native is not null)
        {
            _endpointManager.Invoker.Invoke(() =>
            {
                call.InitiateOutgoingCall();
            });
        }

        return call;
    }

    public ISipCall MakeCall(string destinationUri, IEnumerable<SipHeader> headers)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var call = new ManagedCall(this, destinationUri, CallDirection.Outgoing, _endpointManager, _logger, headers);
        lock (_lock) _activeCalls.Add(call);
        call.StateChanged += OnCallStateChanged;

        if (NativeAvailability.IsAvailable && _native is not null)
        {
            _endpointManager.Invoker.Invoke(() =>
            {
                call.InitiateOutgoingCall();
            });
        }

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

    /// <summary>
    /// Called from the native bridge when registration state changes.
    /// </summary>
    internal void OnNativeRegState(int statusCode, string reason)
    {
        var oldState = RegistrationState;
        RegistrationState = statusCode / 100 == 2
            ? SipRegistrationState.Registered
            : SipRegistrationState.Error;

        _logger.LogInformation("Registration state for {Uri}: {State} (code={Code} reason={Reason})",
            Uri, RegistrationState, statusCode, reason);

        OnRegistrationStateChanged(oldState, RegistrationState);
    }

    /// <summary>
    /// Called from the native bridge when an incoming call arrives.
    /// </summary>
    internal void OnNativeIncomingCall(int callId)
    {
        var call = new ManagedCall(this, callId, _endpointManager, _logger);
        OnIncomingCall(call);
    }

    /// <summary>
    /// Called from the native bridge on instant message receipt.
    /// </summary>
    internal void OnNativeInstantMessage(string fromUri, string toUri, string contentType, string body)
    {
        _messaging?.OnMessageReceived(new SipMessage
        {
            From = fromUri,
            To = toUri,
            Body = body,
            ContentType = contentType
        });
    }

    /// <summary>
    /// Called from the native bridge on instant message status.
    /// </summary>
    internal void OnNativeInstantMessageStatus(string toUri, int code, string reason)
    {
        _messaging?.OnMessageStatus(toUri, code, reason);
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

        _native?.Dispose();
        _native = null;
    }

    /// <summary>
    /// SWIG Director subclass of <see cref="Gen.Account"/> that receives
    /// native pjsua2 callbacks and delegates them to the parent ManagedAccount.
    /// </summary>
    internal sealed class NativeAccountBridge : Gen.Account
    {
        private readonly ManagedAccount _managed;
        private readonly ILogger _logger;

        public NativeAccountBridge(ManagedAccount managed, ILogger logger) : base()
        {
            _managed = managed;
            _logger = logger;
        }

        public override void onRegState(OnRegStateParam prm)
        {
            try
            {
                _managed.OnNativeRegState((int)prm.code, prm.reason);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in onRegState callback");
            }
        }

        public override void onRegStarted(OnRegStartedParam prm)
        {
            try
            {
                _logger.LogDebug("Registration started for {Uri}", _managed.Uri);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in onRegStarted callback");
            }
        }

        public override void onIncomingCall(OnIncomingCallParam prm)
        {
            try
            {
                _logger.LogInformation("Incoming call on account {Uri}, callId={CallId}",
                    _managed.Uri, prm.callId);
                _managed.OnNativeIncomingCall(prm.callId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in onIncomingCall callback");
            }
        }

        public override void onInstantMessage(OnInstantMessageParam prm)
        {
            try
            {
                _managed.OnNativeInstantMessage(prm.fromUri, prm.toUri, prm.contentType, prm.msgBody);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in onInstantMessage callback");
            }
        }

        public override void onInstantMessageStatus(OnInstantMessageStatusParam prm)
        {
            try
            {
                _managed.OnNativeInstantMessageStatus(prm.toUri, (int)prm.code, prm.reason);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in onInstantMessageStatus callback");
            }
        }

        public override void onMwiInfo(OnMwiInfoParam prm)
        {
            try
            {
                _logger.LogDebug("MWI info received for {Uri}", _managed.Uri);

                // Parse Messages-Waiting body from SIP NOTIFY (RFC 3842)
                // Format: Messages-Waiting: yes/no\r\nVoice-Message: new/old (new-urgent/old-urgent)
                var body = prm.rdata?.wholeMsg ?? string.Empty;
                bool hasWaiting = body.Contains("Messages-Waiting: yes", StringComparison.OrdinalIgnoreCase);
                int newMsgs = 0, oldMsgs = 0, newUrgent = 0, oldUrgent = 0;

                foreach (var line in body.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("Voice-Message:", StringComparison.OrdinalIgnoreCase))
                    {
                        // Voice-Message: 2/8 (1/2)
                        var value = trimmed["Voice-Message:".Length..].Trim();
                        var parts = value.Split('(');
                        var mainParts = parts[0].Trim().Split('/');
                        if (mainParts.Length >= 2)
                        {
                            int.TryParse(mainParts[0].Trim(), out newMsgs);
                            int.TryParse(mainParts[1].Trim(), out oldMsgs);
                        }
                        if (parts.Length >= 2)
                        {
                            var urgentParts = parts[1].TrimEnd(')').Trim().Split('/');
                            if (urgentParts.Length >= 2)
                            {
                                int.TryParse(urgentParts[0].Trim(), out newUrgent);
                                int.TryParse(urgentParts[1].Trim(), out oldUrgent);
                            }
                        }
                    }
                }

                _managed.OnMwiInfo(new MwiInfo
                {
                    AccountUri = _managed.Uri,
                    HasWaiting = hasWaiting,
                    NewMessages = newMsgs,
                    OldMessages = oldMsgs,
                    NewUrgentMessages = newUrgent,
                    OldUrgentMessages = oldUrgent
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in onMwiInfo callback");
            }
        }
    }
}
