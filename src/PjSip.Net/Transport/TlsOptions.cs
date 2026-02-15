namespace PjSip.Net.Transport;

public sealed class TlsOptions
{
    public string? CertificateFile { get; set; }
    public string? PrivateKeyFile { get; set; }
    public string? CaListFile { get; set; }
    public bool VerifyServer { get; set; } = true;
    public bool VerifyClient { get; set; }
}
