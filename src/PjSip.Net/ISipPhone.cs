using PjSip.Net.Accounts;
using PjSip.Net.Calls;
using PjSip.Net.Conference;
using PjSip.Net.Events;
using PjSip.Net.History;
using PjSip.Net.Media;
using PjSip.Net.Messaging;
using PjSip.Net.Network;
using PjSip.Net.Presence;
using PjSip.Net.Quality;
using PjSip.Net.Recording;
using PjSip.Net.Tones;

namespace PjSip.Net;

public interface ISipPhone : IAsyncDisposable, IDisposable
{
    SipPhoneState State { get; }
    IReadOnlyList<ISipAccount> Accounts { get; }
    ISipAudioManager Audio { get; }
    ISipCodecManager Codecs { get; }
    ISipPresenceManager Presence { get; }
    ISipMessaging Messaging { get; }
    ISipConferenceBridge Conference { get; }
    ISipCallRecorder Recorder { get; }
    ISipToneGenerator Tones { get; }
    ISipCallQualityMonitor Quality { get; }
    ISipCallHistory History { get; }
    ISipNetworkMonitor Network { get; }

    event EventHandler<IncomingCallEventArgs>? IncomingCall;
    event EventHandler<CallStateChangedEventArgs>? CallStateChanged;
    event EventHandler<RegistrationStateChangedEventArgs>? RegistrationStateChanged;
    event EventHandler<TransportStateChangedEventArgs>? TransportStateChanged;
    event EventHandler<MwiStateChangedEventArgs>? MwiStateChanged;

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);

    ISipAccount AddAccount(SipAccountOptions options);
    void RemoveAccount(ISipAccount account);
    ISipCall MakeCall(ISipAccount account, string destinationUri);
    ISipCall MakeCall(ISipAccount account, string destinationUri, IEnumerable<SipHeader> headers);
}
