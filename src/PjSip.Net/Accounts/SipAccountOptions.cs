using PjSip.Net.Transport;

namespace PjSip.Net.Accounts;

public sealed class SipAccountOptions
{
    public required string Username { get; set; }
    public required string Password { get; set; }
    public required string Domain { get; set; }
    public string? Registrar { get; set; }
    public string? OutboundProxy { get; set; }
    public string? DisplayName { get; set; }
    public string? Realm { get; set; }
    public int RegistrationTimeout { get; set; } = 300;
    public bool RegisterOnAdd { get; set; } = true;
    public bool UseTls { get; set; }

    /// <summary>
    /// Explicit transport for this account. When set, all SIP URIs (registrar,
    /// proxy, and outgoing call destinations) include a ";transport=xxx" parameter
    /// so PJSIP uses the correct transport instead of relying on DNS SRV lookup.
    /// When null, falls back to <see cref="UseTls"/> for backwards compatibility.
    /// </summary>
    public SipTransportType? Transport { get; set; }

    /// <summary>
    /// Extra parameters appended to the Contact URI on REGISTER, semicolon-separated and WITHOUT a
    /// leading ';' (PJSIP adds the separator). Primarily for RFC 8599 SIP push so the registrar/PBX
    /// can wake the device via APNs/FCM, e.g.:
    /// <c>pn-provider=apns;pn-prid=&lt;token&gt;;pn-param=&lt;TeamId&gt;.&lt;bundleId&gt;.voip</c>
    /// Maps to pjsua2 <c>AccountConfig.regConfig.contactUriParams</c>. Null/empty = no extra params.
    /// </summary>
    public string? ContactUriParams { get; set; }

    /// <summary>
    /// SRTP (secure media) policy for this account's calls. Default <see cref="SrtpUse.Disabled"/>
    /// preserves the previous behavior. Use <see cref="SrtpUse.Optional"/> to interoperate with
    /// PBXs that offer SRTP (RTP/SAVP) on incoming calls — with Disabled those INVITEs are
    /// rejected with 488 Not Acceptable Here before any call event reaches the app.
    /// Maps to pjsua2 <c>AccountConfig.mediaConfig.srtpUse</c>.
    /// </summary>
    public SrtpUse SrtpUse { get; set; } = SrtpUse.Disabled;

    /// <summary>
    /// Whether SRTP requires the SIP signaling to be secure (TLS) before keys are exchanged
    /// (SDES crypto attributes travel in the SDP, so without TLS they are visible on the wire).
    /// True (the pjsua default) = require a secure transport; set false only for lab/testing
    /// against PBXs that do SRTP over UDP/TCP signaling.
    /// Maps to pjsua2 <c>AccountConfig.mediaConfig.srtpSecureSignaling</c> (1/0).
    /// Ignored when <see cref="SrtpUse"/> is <see cref="SrtpUse.Disabled"/>.
    /// </summary>
    public bool SrtpSecureSignaling { get; set; } = true;
}
