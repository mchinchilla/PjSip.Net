namespace PjSip.Net.Events;

public abstract class SipEventArgs : EventArgs
{
    public DateTime Timestamp { get; } = DateTime.UtcNow;
}
