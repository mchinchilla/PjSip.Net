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
                    Direction = CallDirection.Incoming
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

        _native.makeCall(Info.RemoteUri, prm);
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

        Info = new SipCallInfo
        {
            CallId = Id,
            RemoteUri = ci.remoteUri,
            LocalUri = ci.localUri,
            State = newState,
            Direction = Direction,
            Duration = duration,
            StatusCode = (int)ci.lastStatusCode,
            StatusText = ci.lastReason
        };

        if (newState == SipCallState.Confirmed && _connectTime == default)
            _connectTime = DateTime.UtcNow;

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

                    // Connect to sound device
                    var ep = _endpointManager.Endpoint;
                    if (ep is not null)
                    {
                        var audMgr = ep.audDevManager();
                        var playMedia = audMgr.getPlaybackDevMedia();
                        var capMedia = audMgr.getCaptureDevMedia();

                        _audioMedia.startTransmit(playMedia);
                        capMedia.startTransmit(_audioMedia);
                    }

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

        if (State is not SipCallState.Disconnected and not SipCallState.Null)
        {
            try { Hangup(); }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Best-effort hangup during dispose of call {CallId}", Id);
            }
        }

        _native?.Dispose();
        _native = null;
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
