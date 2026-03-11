using Microsoft.Extensions.Logging;
using PjSip.Net.Calls;
using PjSip.Net.Events;
using PjSip.Net.Interop.Generated;
using Gen = PjSip.Net.Interop.Generated;
using SipHeader = PjSip.Net.Calls.SipHeader;

namespace PjSip.Net.Internal;

/// <summary>
/// Managed-side call that bridges pjsua2 Call callbacks to .NET events.
/// Uses an internal <see cref="NativeCallBridge"/> (SWIG Director) when
/// native binaries are available; otherwise operates in stub mode for tests.
/// </summary>
internal sealed class ManagedCall : ISipCall
{
    private readonly ManagedAccount _account;
    private readonly PjSipEndpointManager _endpointManager;
    private readonly ILogger _logger;
    private readonly List<SipHeader> _customHeaders = [];
    private NativeCallBridge? _native;
    private AudioMedia? _audioMedia;
    private DateTime _connectTime;
    private volatile bool _disposed;

    /// <summary>
    /// Constructor for outgoing calls.
    /// </summary>
    public ManagedCall(
        ManagedAccount account,
        string remoteUri,
        CallDirection direction,
        PjSipEndpointManager endpointManager,
        ILogger logger,
        IEnumerable<SipHeader>? headers = null)
    {
        _account = account;
        _endpointManager = endpointManager;
        _logger = logger;
        Id = Guid.NewGuid().ToString("N")[..8];
        Direction = direction;
        Info = new SipCallInfo
        {
            CallId = Id,
            RemoteUri = remoteUri,
            LocalUri = account.Uri,
            State = SipCallState.Null,
            Direction = direction
        };
        if (headers is not null)
            _customHeaders.AddRange(headers);
    }

    /// <summary>
    /// Constructor for incoming calls (created from native callback with a call ID).
    /// </summary>
    internal ManagedCall(
        ManagedAccount account,
        int nativeCallId,
        PjSipEndpointManager endpointManager,
        ILogger logger)
    {
        _account = account;
        _endpointManager = endpointManager;
        _logger = logger;
        Direction = CallDirection.Incoming;

        if (NativeAvailability.IsAvailable && account.Native is not null)
        {
            _native = new NativeCallBridge(this, account.Native, nativeCallId, logger);

            // Read call info from native
            try
            {
                var ci = _native.getInfo();
                Id = ci.callIdString;
                Info = new SipCallInfo
                {
                    CallId = ci.callIdString,
                    RemoteUri = ci.remoteUri,
                    LocalUri = ci.localUri,
                    State = MapCallState(ci.state),
                    Direction = CallDirection.Incoming,
                    RemoteDisplayName = ExtractDisplayName(ci.remoteUri)
                };
                State = MapCallState(ci.state);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read native call info for incoming call, using defaults");
                Id = Guid.NewGuid().ToString("N")[..8];
                Info = new SipCallInfo
                {
                    CallId = Id,
                    RemoteUri = "unknown",
                    LocalUri = account.Uri,
                    State = SipCallState.Incoming,
                    Direction = CallDirection.Incoming
                };
                State = SipCallState.Incoming;
            }
        }
        else
        {
            Id = Guid.NewGuid().ToString("N")[..8];
            Info = new SipCallInfo
            {
                CallId = Id,
                RemoteUri = "unknown",
                LocalUri = account.Uri,
                State = SipCallState.Incoming,
                Direction = CallDirection.Incoming
            };
            State = SipCallState.Incoming;
        }
    }

    public string Id { get; }
    public SipCallState State { get; private set; } = SipCallState.Null;
    public CallDirection Direction { get; }
    public SipCallInfo Info { get; private set; }
    public IReadOnlyList<SipHeader> CustomHeaders => _customHeaders;
    public bool IsMuted { get; private set; }
    public bool IsOnHold { get; private set; }

    /// <summary>
    /// Parses SIP headers from a raw SIP message and populates <see cref="CustomHeaders"/>.
    /// </summary>
    internal void ParseSipHeaders(string rawSipMessage)
    {
        // SIP headers are separated from the body by a blank line (\r\n\r\n).
        // Each header is on its own line: "Header-Name: value\r\n"
        var headerEnd = rawSipMessage.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var headerSection = headerEnd >= 0 ? rawSipMessage[..headerEnd] : rawSipMessage;

        foreach (var line in headerSection.Split("\r\n"))
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex <= 0) continue;

            var name = line[..colonIndex].Trim();
            var value = line[(colonIndex + 1)..].Trim();

            // Skip standard SIP first-line (e.g. "INVITE sip:...") and empty values
            if (name.Contains(' ') || string.IsNullOrEmpty(value)) continue;

            _customHeaders.Add(new SipHeader { Name = name, Value = value });
        }
    }

    /// <summary>
    /// The connected AudioMedia for this call, if any.
    /// Used by recording, conference bridge, and mute operations.
    /// </summary>
    internal AudioMedia? AudioMedia => _audioMedia;

    /// <summary>
    /// Gets stream statistics for the specified media index via the native call bridge.
    /// Must be called on the PJSIP worker thread.
    /// </summary>
    internal StreamStat? GetStreamStat(uint mediaIndex = 0)
    {
        if (_native is null) return null;
        try { return _native.getStreamStat(mediaIndex); }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get stream stats for call {CallId} media index {Index}", Id, mediaIndex);
            return null;
        }
    }

    /// <summary>
    /// Gets stream info (codec, direction, etc.) for the specified media index via the native call bridge.
    /// Must be called on the PJSIP worker thread.
    /// </summary>
    internal StreamInfo? GetStreamInfo(uint mediaIndex = 0)
    {
        if (_native is null) return null;
        try { return _native.getStreamInfo(mediaIndex); }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get stream info for call {CallId} media index {Index}", Id, mediaIndex);
            return null;
        }
    }

    public event EventHandler<CallStateChangedEventArgs>? StateChanged;
    public event EventHandler<CallMediaStateChangedEventArgs>? MediaStateChanged;

    /// <summary>
    /// Called on the PJSIP worker thread to initiate an outgoing call via native API.
    /// </summary>
    internal void InitiateOutgoingCall()
    {
        if (_account.Native is null) return;

        _native = new NativeCallBridge(this, _account.Native, _logger);

        using var prm = new CallOpParam(true);

        // Add custom headers if present
        if (_customHeaders.Count > 0)
        {
            foreach (var header in _customHeaders)
            {
                var nativeHeader = new Gen.SipHeader
                {
                    hName = header.Name,
                    hValue = header.Value
                };
                prm.txOption.headers.Add(nativeHeader);
            }
        }

        // Pin the destination URI to the account's transport so PJSIP doesn't
        // pick a different transport via DNS SRV (e.g. TLS for a UDP account).
        var destUri = Info.RemoteUri;
        var transportSuffix = _account.GetTransportSuffix();
        if (transportSuffix.Length > 0 && !destUri.Contains(";transport=", StringComparison.OrdinalIgnoreCase))
        {
            destUri += transportSuffix;
        }

        try
        {
            _logger.LogInformation("makeCall: {DestUri} (account transport: {Transport})",
                destUri, transportSuffix.Length > 0 ? transportSuffix : "(default)");
            _native.makeCall(destUri, prm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "makeCall failed for {RemoteUri}", destUri);
            // Clean up the native bridge since the call was never established
            _native.Dispose();
            _native = null;
            SetState(SipCallState.Disconnected);
        }
    }

    public void Answer(int statusCode = 200)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _logger.LogInformation("Answering call {CallId} with status {StatusCode}", Id, statusCode);

        if (_native is not null)
        {
            _endpointManager.Invoker.Invoke(() =>
            {
                using var prm = new CallOpParam();
                prm.statusCode = (pjsip_status_code)statusCode;
                _native.answer(prm);
            });
        }
        else
        {
            SetState(SipCallState.Connecting);
        }
    }

    public void Answer(int statusCode, IEnumerable<SipHeader> headers)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _logger.LogInformation("Answering call {CallId} with status {StatusCode} and custom headers", Id, statusCode);

        if (_native is not null)
        {
            _endpointManager.Invoker.Invoke(() =>
            {
                using var prm = new CallOpParam();
                prm.statusCode = (pjsip_status_code)statusCode;
                foreach (var header in headers)
                {
                    var nativeHeader = new Gen.SipHeader
                    {
                        hName = header.Name,
                        hValue = header.Value
                    };
                    prm.txOption.headers.Add(nativeHeader);
                }
                _native.answer(prm);
            });
        }
        else
        {
            SetState(SipCallState.Connecting);
        }
    }

    public void Hangup(int statusCode = 603)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _logger.LogInformation("Hanging up call {CallId} with status {StatusCode}", Id, statusCode);

        if (_native is not null)
        {
            _endpointManager.Invoker.Invoke(() =>
            {
                using var prm = new CallOpParam();
                prm.statusCode = (pjsip_status_code)statusCode;
                try
                {
                    _native.hangup(prm);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error hanging up call {CallId} natively", Id);
                }
            });
        }

        SetState(SipCallState.Disconnected);
    }

    public void Hold()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _logger.LogDebug("Holding call {CallId}", Id);
        IsOnHold = true;

        if (_native is not null)
        {
            _endpointManager.Invoker.Invoke(() =>
            {
                using var prm = new CallOpParam(true);
                _native.setHold(prm);
            });
        }
    }

    public void Unhold()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _logger.LogDebug("Unholding call {CallId}", Id);
        IsOnHold = false;

        if (_native is not null)
        {
            _endpointManager.Invoker.Invoke(() =>
            {
                using var prm = new CallOpParam(true);
                prm.opt.flag = (uint)pjsua_call_flag.PJSUA_CALL_UNHOLD;
                _native.reinvite(prm);
            });
        }
    }

    public void Transfer(string destinationUri)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _logger.LogInformation("Transferring call {CallId} to {Destination}", Id, destinationUri);

        if (_native is not null)
        {
            _endpointManager.Invoker.Invoke(() =>
            {
                using var prm = new CallOpParam();
                _native.xfer(destinationUri, prm);
            });
        }
    }

    public void AttendedTransfer(ISipCall targetCall)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _logger.LogInformation("Attended transfer call {CallId} to {TargetCallId}", Id, targetCall.Id);

        if (_native is not null && targetCall is ManagedCall targetManaged && targetManaged._native is not null)
        {
            _endpointManager.Invoker.Invoke(() =>
            {
                using var prm = new CallOpParam();
                _native.xferReplaces(targetManaged._native, prm);
            });
        }
    }

    private static readonly HashSet<char> ValidDtmfChars = ['0','1','2','3','4','5','6','7','8','9','*','#','A','B','C','D'];

    public void SendDtmf(string digits)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(digits);
        foreach (var c in digits)
        {
            if (!ValidDtmfChars.Contains(c))
                throw new ArgumentException($"Invalid DTMF digit: '{c}'. Allowed: 0-9, *, #, A-D.", nameof(digits));
        }

        _logger.LogDebug("Sending DTMF {Digits} on call {CallId}", digits, Id);

        if (_native is not null)
        {
            _endpointManager.Invoker.Invoke(() =>
            {
                _native.dialDtmf(digits);
            });
        }
    }

    public void SetMute(bool mute)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IsMuted = mute;
        _logger.LogDebug("Setting mute={Mute} on call {CallId}", mute, Id);

        if (_native is not null && _audioMedia is not null)
        {
            _endpointManager.Invoker.Invoke(() =>
            {
                try
                {
                    var ep = _endpointManager.Endpoint;
                    if (ep is null) return;
                    var capMedia = ep.audDevManager().getCaptureDevMedia();

                    if (mute)
                        capMedia.stopTransmit(_audioMedia);
                    else
                        capMedia.startTransmit(_audioMedia);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error setting mute on call {CallId}", Id);
                }
            });
        }
    }

    internal void SetState(SipCallState newState)
    {
        var oldState = State;
        if (oldState == newState) return;
        State = newState;

        if (newState == SipCallState.Confirmed)
            _connectTime = DateTime.UtcNow;

        var duration = newState == SipCallState.Disconnected && _connectTime != default
            ? DateTime.UtcNow - _connectTime
            : TimeSpan.Zero;

        Info = Info with { State = newState, Duration = duration };

        StateChanged?.Invoke(this, new CallStateChangedEventArgs
        {
            Call = this,
            OldState = oldState,
            NewState = newState
        });
    }

    internal void OnMediaStateChanged(bool isActive)
    {
        MediaStateChanged?.Invoke(this, new CallMediaStateChangedEventArgs
        {
            Call = this,
            IsActive = isActive
        });
    }

    /// <summary>
    /// Called from the native bridge when call state changes.
    /// </summary>
    internal void OnNativeCallState(pjsip_inv_state nativeState, Gen.CallInfo ci)
    {
        var newState = MapCallState(nativeState);

        var duration = ci.connectDuration is not null
            ? TimeSpan.FromSeconds(ci.connectDuration.sec) + TimeSpan.FromMilliseconds(ci.connectDuration.msec)
            : TimeSpan.Zero;

        var statusCode = (int)ci.lastStatusCode;
        var statusText = ci.lastReason;

        Info = new SipCallInfo
        {
            CallId = Id,
            RemoteUri = ci.remoteUri,
            LocalUri = ci.localUri,
            State = newState,
            Direction = Direction,
            Duration = duration,
            StatusCode = statusCode,
            StatusText = statusText,
            RemoteDisplayName = ExtractDisplayName(ci.remoteUri) ?? Info.RemoteDisplayName
        };

        _logger.LogInformation(
            "Call {CallId} state: {State} (remote={Remote}, code={StatusCode}, reason={Reason})",
            Id, newState, ci.remoteUri, statusCode, string.IsNullOrEmpty(statusText) ? "(none)" : statusText);

        if (newState == SipCallState.Confirmed && _connectTime == default)
            _connectTime = DateTime.UtcNow;

        // On iOS, restore null sound device after call disconnect so the next
        // makeCall succeeds. Without this, the audio subsystem can be left in
        // a corrupted state that prevents subsequent calls.
        if (newState == SipCallState.Disconnected && _endpointManager.IsIOS)
        {
            _endpointManager.Invoker.Invoke(() => _endpointManager.RestoreNullDeviceIfIOS());
        }

        SetState(newState);
    }

    /// <summary>
    /// Called from the native bridge when media state changes.
    /// </summary>
    internal void OnNativeMediaState()
    {
        if (_native is null) return;

        try
        {
            var ci = _native.getInfo();
            for (uint i = 0; i < ci.media.Count; i++)
            {
                var mi = ci.media[(int)i];
                if (mi.type == pjmedia_type.PJMEDIA_TYPE_AUDIO &&
                    mi.status == pjsua_call_media_status.PJSUA_CALL_MEDIA_ACTIVE)
                {
                    _audioMedia = _native.getAudioMedia((int)i);

                    // Connect to sound device — MUST run on the PJSIP worker thread
                    // to avoid pj_mutex_lock assertion failures (SIGABRT).
                    var audioMedia = _audioMedia;
                    _endpointManager.Invoker.Invoke(() =>
                    {
                        try
                        {
                            var ep = _endpointManager.Endpoint;
                            if (ep is not null && audioMedia is not null)
                            {
                                // On iOS, try switching from null device to real CoreAudio.
                                // If it fails, we keep the null device — call stays connected
                                // but without real audio (better than crashing).
                                if (_endpointManager.IsIOS)
                                {
                                    _endpointManager.TrySwitchToRealAudioDevice();
                                }

                                var audMgr = ep.audDevManager();

                                var playMedia = audMgr.getPlaybackDevMedia();
                                var capMedia = audMgr.getCaptureDevMedia();

                                audioMedia.startTransmit(playMedia);
                                capMedia.startTransmit(audioMedia);

                                // Log the actual audio devices in use
                                try
                                {
                                    var capId = audMgr.getCaptureDev();
                                    var playId = audMgr.getPlaybackDev();
                                    var capInfo = audMgr.getDevInfo(capId);
                                    var playInfo = audMgr.getDevInfo(playId);
                                    _logger.LogInformation("AUDIO IN:  [{CapId}] {CapName}", capId, capInfo.name);
                                    _logger.LogInformation("AUDIO OUT: [{PlayId}] {PlayName}", playId, playInfo.name);
                                }
                                catch (Exception devEx)
                                {
                                    _logger.LogDebug(devEx, "Could not log audio device info for call {CallId}", Id);
                                }

                                _logger.LogInformation("Media connected to sound device for call {CallId}", Id);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error connecting media to sound device for call {CallId}", Id);
                        }
                    });

                    OnMediaStateChanged(true);
                    return;
                }
            }

            _audioMedia = null;
            OnMediaStateChanged(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error handling media state for call {CallId}", Id);
        }
    }

    /// <summary>
    /// Extracts the display name from a SIP URI of the form: "Display Name" &lt;sip:user@domain&gt;
    /// Returns null if no display name is present.
    /// </summary>
    internal static string? ExtractDisplayName(string? uri)
    {
        if (string.IsNullOrEmpty(uri)) return null;
        // Format: "Display Name" <sip:user@domain> or Display Name <sip:user@domain>
        var ltIndex = uri.IndexOf('<');
        if (ltIndex <= 0) return null;
        var name = uri[..ltIndex].Trim().Trim('"').Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    internal static SipCallState MapCallState(pjsip_inv_state state)
    {
        return state switch
        {
            pjsip_inv_state.PJSIP_INV_STATE_NULL => SipCallState.Null,
            pjsip_inv_state.PJSIP_INV_STATE_CALLING => SipCallState.Calling,
            pjsip_inv_state.PJSIP_INV_STATE_INCOMING => SipCallState.Incoming,
            pjsip_inv_state.PJSIP_INV_STATE_EARLY => SipCallState.EarlyMedia,
            pjsip_inv_state.PJSIP_INV_STATE_CONNECTING => SipCallState.Connecting,
            pjsip_inv_state.PJSIP_INV_STATE_CONFIRMED => SipCallState.Confirmed,
            pjsip_inv_state.PJSIP_INV_STATE_DISCONNECTED => SipCallState.Disconnected,
            _ => SipCallState.Null
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);

        if (State is not SipCallState.Disconnected and not SipCallState.Null)
        {
            try { Hangup(); }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Best-effort hangup during dispose of call {CallId}", Id);
            }
        }

        // The SWIG Call destructor calls native PJSIP APIs that require the
        // calling thread to be registered with pjlib. Dispatch to the PJSIP
        // worker thread to avoid SIGABRT from pj_thread_this() assertion.
        if (_native is not null)
        {
            var native = _native;
            _native = null;
            try
            {
                void DisposeNative()
                {
                    native.Dispose();
                    if (_endpointManager.IsIOS)
                        _endpointManager.RestoreNullDeviceIfIOS();
                }

                if (_endpointManager.Invoker.IsOnPjThread)
                    DisposeNative();
                else
                    _endpointManager.Invoker.InvokeAsync(DisposeNative).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing native call {CallId}", Id);
            }
        }
    }

    ~ManagedCall()
    {
        if (_disposed || _native is null) return;
        _disposed = true;

        // GC Finalizer thread is not registered with pjlib — dispatching
        // native disposal to the PJSIP worker thread prevents SIGABRT
        // from pj_thread_this() assertion failure.
        var native = _native;
        _native = null;
        try
        {
            _endpointManager.Invoker.Invoke(() => native.Dispose());
        }
        catch
        {
            // Best-effort during finalization; invoker may already be disposed.
        }
    }

    /// <summary>
    /// SWIG Director subclass of <see cref="Gen.Call"/> that receives
    /// native pjsua2 callbacks and delegates them to the parent ManagedCall.
    /// </summary>
    internal sealed class NativeCallBridge : Gen.Call
    {
        private readonly ManagedCall _managed;
        private readonly ILogger _logger;

        /// <summary>Constructor for outgoing calls.</summary>
        public NativeCallBridge(ManagedCall managed, Gen.Account account, ILogger logger)
            : base(account)
        {
            _managed = managed;
            _logger = logger;
        }

        /// <summary>Constructor for incoming calls with existing call ID.</summary>
        public NativeCallBridge(ManagedCall managed, Gen.Account account, int callId, ILogger logger)
            : base(account, callId)
        {
            _managed = managed;
            _logger = logger;
        }

        public override void onCallState(OnCallStateParam prm)
        {
            try
            {
                var ci = getInfo();
                _managed.OnNativeCallState(ci.state, ci);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in onCallState callback");
            }
        }

        public override void onCallMediaState(OnCallMediaStateParam prm)
        {
            try
            {
                _managed.OnNativeMediaState();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in onCallMediaState callback");
            }
        }

        public override void onDtmfDigit(OnDtmfDigitParam prm)
        {
            try
            {
                _logger.LogDebug("DTMF digit received: {Digit} on call {CallId}",
                    prm.digit, _managed.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in onDtmfDigit callback");
            }
        }

        public override void onCallTransferStatus(OnCallTransferStatusParam prm)
        {
            try
            {
                _logger.LogDebug("Transfer status for call {CallId}: code={Code}",
                    _managed.Id, prm.statusCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in onCallTransferStatus callback");
            }
        }
    }
}
