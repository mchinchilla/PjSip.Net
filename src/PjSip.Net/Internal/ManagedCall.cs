using Microsoft.Extensions.Logging;
using PjSip.Net.Calls;
using PjSip.Net.Events;

namespace PjSip.Net.Internal;

/// <summary>
/// Internal adapter that will subclass pj.Call to bridge pjsua2 callbacks
/// to .NET events. Until SWIG-generated classes are available, this serves
/// as the managed-side call state holder.
/// </summary>
internal sealed class ManagedCall : ISipCall
{
    private readonly ManagedAccount _account;
    private readonly PjSipEndpointManager _endpointManager;
    private readonly ILogger _logger;
    private readonly List<SipHeader> _customHeaders = [];
    private volatile bool _disposed;

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

    public string Id { get; }
    public SipCallState State { get; private set; } = SipCallState.Null;
    public CallDirection Direction { get; }
    public SipCallInfo Info { get; private set; }
    public IReadOnlyList<SipHeader> CustomHeaders => _customHeaders;
    public bool IsMuted { get; private set; }
    public bool IsOnHold { get; private set; }

    public event EventHandler<CallStateChangedEventArgs>? StateChanged;
    public event EventHandler<CallMediaStateChangedEventArgs>? MediaStateChanged;

    public void Answer(int statusCode = 200)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _logger.LogInformation("Answering call {CallId} with status {StatusCode}", Id, statusCode);

        // TODO: When SWIG-generated classes are available:
        // nativeCall.answer(CallOpParam(statusCode))
        SetState(SipCallState.Connecting);
    }

    public void Answer(int statusCode, IEnumerable<SipHeader> headers)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _logger.LogInformation("Answering call {CallId} with status {StatusCode} and custom headers", Id, statusCode);

        // TODO: When SWIG-generated classes are available:
        // 1. Create CallOpParam with headers in SipTxOption
        // 2. nativeCall.answer(callOpParam)
        SetState(SipCallState.Connecting);
    }

    public void Hangup(int statusCode = 603)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _logger.LogInformation("Hanging up call {CallId} with status {StatusCode}", Id, statusCode);

        // TODO: When SWIG-generated classes are available:
        // nativeCall.hangup(CallOpParam(statusCode))
        SetState(SipCallState.Disconnected);
    }

    public void Hold()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _logger.LogDebug("Holding call {CallId}", Id);
        IsOnHold = true;
        // TODO: nativeCall.setHold(CallOpParam())
    }

    public void Unhold()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _logger.LogDebug("Unholding call {CallId}", Id);
        IsOnHold = false;
        // TODO: CallOpParam with SDP reinvite
    }

    public void Transfer(string destinationUri)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _logger.LogInformation("Transferring call {CallId} to {Destination}", Id, destinationUri);
        // TODO: nativeCall.xfer(destinationUri, CallOpParam())
    }

    public void AttendedTransfer(ISipCall targetCall)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _logger.LogInformation("Attended transfer call {CallId} to {TargetCallId}", Id, targetCall.Id);
        // TODO: nativeCall.xferReplaces(targetManagedCall, CallOpParam())
    }

    public void SendDtmf(string digits)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _logger.LogDebug("Sending DTMF {Digits} on call {CallId}", digits, Id);
        // TODO: nativeCall.dialDtmf(digits)
    }

    public void SetMute(bool mute)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IsMuted = mute;
        _logger.LogDebug("Setting mute={Mute} on call {CallId}", mute, Id);
        // TODO: Adjust audio media conference port connection
    }

    internal void SetState(SipCallState newState)
    {
        var oldState = State;
        if (oldState == newState) return;
        State = newState;

        Info = Info with { State = newState };

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (State is not SipCallState.Disconnected and not SipCallState.Null)
        {
            try { Hangup(); } catch { /* best effort */ }
        }
    }
}
