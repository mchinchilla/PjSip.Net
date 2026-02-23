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
    /// Optional callback invoked on iOS just before PJSIP opens the real
    /// CoreAudio device. Use this to (re-)activate the platform audio session
    /// (e.g. AVAudioSession) so that VoiceProcessingIO can initialize.
    /// </summary>
    public Action? OnAudioDeviceActivation { get; set; }
}
