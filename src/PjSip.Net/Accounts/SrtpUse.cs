namespace PjSip.Net.Accounts;

/// <summary>
/// SRTP (secure media) policy for an account. Maps 1:1 to pjsua2's
/// <c>pjmedia_srtp_use</c> on <c>AccountConfig.mediaConfig.srtpUse</c>.
/// </summary>
public enum SrtpUse
{
    /// <summary>SRTP is not offered; incoming SRTP-only (RTP/SAVP) offers are rejected with 488.</summary>
    Disabled = 0,

    /// <summary>
    /// SRTP is offered alongside plain RTP and accepted when the peer offers it.
    /// Interoperates with both secure and non-secure peers — the recommended setting
    /// when the PBX enforces SRTP on some legs only.
    /// </summary>
    Optional = 1,

    /// <summary>SRTP is required: offers are SRTP-only and non-SRTP answers/offers are rejected.</summary>
    Mandatory = 2
}
