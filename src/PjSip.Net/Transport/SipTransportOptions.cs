namespace PjSip.Net.Transport;

public sealed class SipTransportOptions
{
    public SipTransportType Type { get; set; } = SipTransportType.Udp;
    public int Port { get; set; }
    public string? BoundAddress { get; set; }
    public TlsOptions? Tls { get; set; }
}
