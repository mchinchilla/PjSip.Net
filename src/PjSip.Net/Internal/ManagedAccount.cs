using Microsoft.Extensions.Logging;
using PjSip.Net.Accounts;
using PjSip.Net.Calls;
using PjSip.Net.Events;
using PjSip.Net.Interop.Generated;
using PjSip.Net.Messaging;
using PjSip.Net.Transport;
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
        var sipUser = options.Username.Replace("@", "%40");
        Uri = $"sip:{sipUser}@{options.Domain}";
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

    /// <summary>
    /// Returns the ";transport=xxx" suffix for this account's transport,
    /// so all SIP URIs (registrar, proxy, call destinations) are pinned
    /// to the correct transport.
    /// </summary>
    internal string GetTransportSuffix()
    {
        if (Options.Transport.HasValue)
        {
            return Options.Transport.Value switch
            {
                SipTransportType.Tcp => ";transport=tcp",
                SipTransportType.Tls => ";transport=tls",
                SipTransportType.Tcp6 => ";transport=tcp",
                SipTransportType.Tls6 => ";transport=tls",
                // UDP is the default in SIP, but we add it explicitly
                // to prevent PJSIP from choosing a different transport via DNS SRV.
                _ => ";transport=udp"
            };
        }

        // Backwards compatibility: fall back to UseTls flag
        return Options.UseTls ? ";transport=tls" : "";
    }

    /// <summary>
    /// The transport this account is configured to use, or null when it expressed no preference
    /// (no <see cref="SipAccountOptions.Transport"/> and <c>UseTls</c> false) — in that case PJSIP's
    /// own URI-driven selection is left alone.
    /// </summary>
    private SipTransportType? ResolveTransportType() =>
        Options.Transport ?? (Options.UseTls ? SipTransportType.Tls : null);

    /// <summary>
    /// Pins the account to its transport via <c>sipConfig.transportId</c>.
    ///
    /// Without this every account sits on PJSUA_INVALID_ID, which makes
    /// <c>pjsua_init_tpselector()</c> (pjsua_core.c) return without setting a selector — so PJSIP
    /// derives the transport of each out-of-dialog request from the target URI alone, and a URI
    /// with no <c>;transport=</c> param falls back to UDP (RFC 3263, sip_resolve.c). On a TLS
    /// account that silently sent buddy SUBSCRIBE, MWI SUBSCRIBE and MESSAGE out over UDP while
    /// REGISTER and INVITE (which carry the suffix) worked — presence stayed dead with 401/408.
    ///
    /// The transport-param suffixes remain as a second line of defence: they also stop DNS SRV from
    /// picking a different transport for the *target*, which the selector does not govern.
    /// </summary>
    private void ApplyTransportId(AccountConfig acfg)
    {
        var type = ResolveTransportType();
        if (type is null)
        {
            _logger.LogDebug("Account {Uri}: no transport preference — leaving PJSIP URI-based selection", Uri);
            return;
        }

        if (_endpointManager.TryGetTransportId(type.Value, out var transportId))
        {
            acfg.sipConfig.transportId = transportId;
            _logger.LogInformation("Account {Uri} pinned to {Transport} transport (id={Id})",
                Uri, type.Value, transportId);
        }
        else
        {
            // Guessing an id here would pin the account to the wrong transport, which is worse than
            // the URI-driven default. Loud, because it means the caller asked for a transport it
            // never created and everything this account sends will pick its own transport.
            _logger.LogWarning(
                "Account {Uri} wants {Transport} but no such transport was created — " +
                "requests will fall back to URI-based transport selection (UDP unless the URI says otherwise)",
                Uri, type.Value);
        }
    }

    /// <summary>
    /// Normalizes Contact-URI params for pjsua2 <c>regConfig.contactUriParams</c>: trims, returns
    /// null for null/empty/whitespace, and ensures exactly one leading ';' (PJSIP appends the value
    /// raw to the Contact URI and expects the leading separator). Used for RFC 8599 push params.
    /// </summary>
    internal static string? NormalizeContactUriParams(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var p = value.Trim();
        return p.StartsWith(';') ? p : ";" + p;
    }

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

            // Already created natively — re-registering must refresh, not create()
            // again (pjsua2 Account.create throws on a second call).
            if (_native is not null && _native.getId() >= 0)
            {
                _native.setRegistration(true);
                return;
            }

            // Create the native bridge if not yet created
            _native ??= NativeAccountBridge.Create(this, _logger);

            using var acfg = new AccountConfig();
            var sipUser = Options.Username.Replace("@", "%40");
            var transportSuffix = GetTransportSuffix();

            acfg.idUri = Options.DisplayName is not null
                ? $"\"{Options.DisplayName}\" <sip:{sipUser}@{Options.Domain}>"
                : $"sip:{sipUser}@{Options.Domain}";

            // Registration config — ensure registrar URI has sip: or sips: scheme
            var registrar = Options.Registrar ?? Options.Domain;
            if (!registrar.StartsWith("sip:", StringComparison.OrdinalIgnoreCase) &&
                !registrar.StartsWith("sips:", StringComparison.OrdinalIgnoreCase))
            {
                registrar = $"sip:{registrar}";
            }
            acfg.regConfig.registrarUri = $"{registrar}{transportSuffix}";
            acfg.regConfig.timeoutSec = (uint)Options.RegistrationTimeout;

            // RFC 8599 push: extra Contact-URI params (pn-provider/pn-prid/pn-param). PJSIP appends
            // contactUriParams raw to the Contact URI and expects a leading ';'.
            var contactParams = NormalizeContactUriParams(Options.ContactUriParams);
            if (contactParams is not null)
            {
                acfg.regConfig.contactUriParams = contactParams;
                _logger.LogInformation("Account contactUriParams (push): {Params}", contactParams);
            }

            _logger.LogInformation(
                "Account config: id={Id}, registrar={Registrar}, transport={Transport}",
                acfg.idUri, acfg.regConfig.registrarUri, transportSuffix.Length > 0 ? transportSuffix : "(default)");

            // Must run before create(): pjsua reads sipConfig.transportId when the account is added.
            ApplyTransportId(acfg);

            // Outbound proxy
            if (!string.IsNullOrEmpty(Options.OutboundProxy))
            {
                var proxy = Options.OutboundProxy;
                if (!proxy.StartsWith("sip:", StringComparison.OrdinalIgnoreCase) &&
                    !proxy.StartsWith("sips:", StringComparison.OrdinalIgnoreCase))
                {
                    proxy = $"sip:{proxy}";
                }
                acfg.sipConfig.proxies.Add($"{proxy}{transportSuffix}");
            }

            // Auth credentials
            var cred = new AuthCredInfo(
                "digest",
                Options.Realm ?? "*",
                Options.Username,
                0, // plain text
                Options.Password);
            acfg.sipConfig.authCreds.Add(cred);

            // SRTP (secure media) policy
            if (Options.SrtpUse != SrtpUse.Disabled)
            {
                acfg.mediaConfig.srtpUse = Options.SrtpUse == SrtpUse.Mandatory
                    ? pjmedia_srtp_use.PJMEDIA_SRTP_MANDATORY
                    : pjmedia_srtp_use.PJMEDIA_SRTP_OPTIONAL;
                acfg.mediaConfig.srtpSecureSignaling = Options.SrtpSecureSignaling ? 1 : 0;
                _logger.LogInformation(
                    "Account SRTP: {SrtpUse} (secureSignaling={SecureSignaling})",
                    Options.SrtpUse, Options.SrtpSecureSignaling);
            }

            try
            {
                _native.create(acfg);
            }
            catch
            {
                // A failed create leaves the bridge with pjsua id -1; calling
                // makeCall on it later trips a native assert and aborts the
                // process. Tear the bridge down so the account is visibly broken.
                var failed = _native;
                _native = null;
                try { failed.Dispose(); }
                catch (Exception disposeEx) { _logger.LogWarning(disposeEx, "Error disposing account bridge after failed create"); }

                RegistrationState = SipRegistrationState.Error;
                OnRegistrationStateChanged(SipRegistrationState.Registering, SipRegistrationState.Error);
                throw;
            }
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
        ThrowIfNativeAccountUnusable();
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
        ThrowIfNativeAccountUnusable();
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

    /// <summary>
    /// Guards outgoing calls against a missing or never-created native account.
    /// pjsua_call_make_call ASSERTS (SIGABRT, whole-process abort) when given an
    /// invalid acc_id, so this must be caught managed-side. Account.getId() is a
    /// plain member read, safe from any thread. Stub mode (no native binaries) is
    /// intentionally allowed through for tests.
    /// </summary>
    private void ThrowIfNativeAccountUnusable()
    {
        if (!NativeAvailability.IsAvailable)
            return;

        if (_native is null || _native.getId() < 0)
            throw new InvalidOperationException(
                $"Account {Uri} has no active native registration (state={RegistrationState}); cannot place a call.");
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
    internal void OnNativeIncomingCall(int callId, string? rawSipMessage = null)
    {
        var call = new ManagedCall(this, callId, _endpointManager, _logger);
        if (!string.IsNullOrEmpty(rawSipMessage))
        {
            call.ParseSipHeaders(rawSipMessage);

            // Dumped here, before the event fires, so the offer still reaches the log when
            // pjsua rejects the call while building the SDP answer — that path never rings
            // and leaves only a status code behind.
            if (_endpointManager.Options.LogIncomingInvites)
                _logger.LogInformation(
                    "Incoming INVITE on {Uri} (callId={CallId}), secrets redacted:\n{Invite}",
                    Uri, callId, SipMessageRedactor.Redact(rawSipMessage));
        }
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
    /// Called from the native bridge on typing indication receipt.
    /// </summary>
    internal void OnNativeTypingIndication(string fromUri, bool isTyping)
    {
        _messaging?.OnTypingIndication(fromUri, isTyping);
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

        // The SWIG Account destructor calls pjsua_acc_del which requires the
        // calling thread to be registered with pjlib. Dispatch to the PJSIP
        // worker thread to avoid SIGABRT from pj_thread_this() assertion.
        if (_native is not null)
        {
            var native = _native;
            _native = null;
            try
            {
                if (_endpointManager.Invoker.IsOnPjThread)
                    native.Dispose();
                else
                    _endpointManager.Invoker.InvokeAsync(() => native.Dispose()).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing native account");
            }
        }
    }

    /// <summary>
    /// SWIG Director subclass of <see cref="Gen.Account"/> that receives
    /// native pjsua2 callbacks and delegates them to the parent ManagedAccount.
    /// </summary>
    internal sealed class NativeAccountBridge : Gen.Account
    {
        private readonly ManagedAccount _managed;
        private readonly ILogger _logger;

        private NativeAccountBridge(ManagedAccount managed, ILogger logger) : base()
        {
            _managed = managed;
            _logger = logger;
        }

        /// <summary>
        /// Creates the bridge with its SWIG finalizer suppressed. The generated
        /// <see cref="Gen.Account"/> base finalizer runs <c>delete_Account()</c>
        /// (→ pjsua_acc_del) on the GC Finalizer thread, which is not registered
        /// with pjlib and would abort the process. The native account is instead
        /// destroyed only via <see cref="ManagedAccount.Dispose"/>, which
        /// marshals onto the PJSIP worker thread.
        /// </summary>
        public static NativeAccountBridge Create(ManagedAccount managed, ILogger logger)
        {
            var bridge = new NativeAccountBridge(managed, logger);
            GC.SuppressFinalize(bridge);
            return bridge;
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
                _managed.OnNativeIncomingCall(prm.callId, prm.rdata?.wholeMsg);
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

        public override void onTypingIndication(OnTypingIndicationParam prm)
        {
            try
            {
                _managed.OnNativeTypingIndication(prm.fromUri, prm.isTyping);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in onTypingIndication callback");
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
