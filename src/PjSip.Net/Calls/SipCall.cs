using PjSip.Net.Events;
using PjSip.Net.Internal;

namespace PjSip.Net.Calls;

/// <summary>
/// Public-facing SipCall that delegates to the internal ManagedCall.
/// </summary>
internal sealed class SipCall : ISipCall
{
    private readonly ManagedCall _inner;

    internal SipCall(ManagedCall inner)
    {
        _inner = inner;
        _inner.StateChanged += (s, e) => StateChanged?.Invoke(this, e);
        _inner.MediaStateChanged += (s, e) => MediaStateChanged?.Invoke(this, e);
    }

    public string Id => _inner.Id;
    public SipCallState State => _inner.State;
    public CallDirection Direction => _inner.Direction;
    public SipCallInfo Info => _inner.Info;
    public IReadOnlyList<SipHeader> CustomHeaders => _inner.CustomHeaders;
    public bool IsMuted => _inner.IsMuted;
    public bool IsOnHold => _inner.IsOnHold;

    public event EventHandler<CallStateChangedEventArgs>? StateChanged;
    public event EventHandler<CallMediaStateChangedEventArgs>? MediaStateChanged;

    public void Answer(int statusCode = 200) => _inner.Answer(statusCode);
    public void Answer(int statusCode, IEnumerable<SipHeader> headers) => _inner.Answer(statusCode, headers);
    public void Hangup(int statusCode = 603) => _inner.Hangup(statusCode);
    public void Hold() => _inner.Hold();
    public void Unhold() => _inner.Unhold();
    public void Transfer(string destinationUri) => _inner.Transfer(destinationUri);
    public void AttendedTransfer(ISipCall targetCall) => _inner.AttendedTransfer(targetCall);
    public void SendDtmf(string digits) => _inner.SendDtmf(digits);
    public void SetMute(bool mute) => _inner.SetMute(mute);

    public void Dispose() => _inner.Dispose();
}
