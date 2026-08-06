using PjSip.Net.Accounts;
using PjSip.Net.Transport;

namespace PjSip.Net;

public sealed class SipPhoneOptions
{
    public string UserAgent { get; set; } = "PjSip.Net/1.0";
    public int LogLevel { get; set; } = 4;
    public List<SipTransportOptions> Transports { get; set; } = [];
    public List<SipAccountOptions> Accounts { get; set; } = [];
    public int MaxCalls { get; set; } = 4;
    public bool UseCompactForm { get; set; }
    public NatOptions Nat { get; set; } = new();
    public int CallHistoryMaxEntries { get; set; } = 1000;

    /// <summary>
    /// Dumps the raw INVITE (headers + SDP body) of every incoming call to the log at
    /// Information level, with SRTP key material and auth credentials redacted first.
    ///
    /// Off by default: each dump costs ~1-2 KB of log per call. Turn it on to diagnose
    /// calls the stack rejects locally during SDP answer creation — those never ring and
    /// leave nothing behind but a bare status code, so the offer that caused the rejection
    /// is otherwise unrecoverable.
    /// </summary>
    public bool LogIncomingInvites { get; set; }

    /// <summary>
    /// Optional callback invoked on iOS just before PJSIP opens the real
    /// CoreAudio device. Use this to (re-)activate the platform audio session
    /// (e.g. AVAudioSession) so that VoiceProcessingIO can initialize.
    /// </summary>
    public Action? OnAudioDeviceActivation { get; set; }
}
