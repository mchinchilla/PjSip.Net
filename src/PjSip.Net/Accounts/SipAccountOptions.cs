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
}
