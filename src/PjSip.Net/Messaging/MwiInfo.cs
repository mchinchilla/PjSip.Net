namespace PjSip.Net.Messaging;

public sealed record MwiInfo
{
    public bool HasWaiting { get; init; }
    public int NewMessages { get; init; }
    public int OldMessages { get; init; }
    public int NewUrgentMessages { get; init; }
    public int OldUrgentMessages { get; init; }
    public string? AccountUri { get; init; }
}
