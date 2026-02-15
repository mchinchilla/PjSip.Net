using PjSip.Net.Events;

namespace PjSip.Net.Calls;

public interface ISipCall : IDisposable
{
    string Id { get; }
    SipCallState State { get; }
    CallDirection Direction { get; }
    SipCallInfo Info { get; }
    IReadOnlyList<SipHeader> CustomHeaders { get; }
    bool IsMuted { get; }
    bool IsOnHold { get; }

    event EventHandler<CallStateChangedEventArgs>? StateChanged;
    event EventHandler<CallMediaStateChangedEventArgs>? MediaStateChanged;

    void Answer(int statusCode = 200);
    void Answer(int statusCode, IEnumerable<SipHeader> headers);
    void Hangup(int statusCode = 603);
    void Hold();
    void Unhold();
    void Transfer(string destinationUri);
    void AttendedTransfer(ISipCall targetCall);
    void SendDtmf(string digits);
    void SetMute(bool mute);
}
