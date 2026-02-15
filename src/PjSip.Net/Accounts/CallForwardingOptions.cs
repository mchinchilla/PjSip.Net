namespace PjSip.Net.Accounts;

public sealed class CallForwardingOptions
{
    public bool Enabled { get; set; }
    public CallForwardingType Type { get; set; } = CallForwardingType.Unconditional;
    public string? DestinationUri { get; set; }
    public TimeSpan NoAnswerTimeout { get; set; } = TimeSpan.FromSeconds(20);
}
