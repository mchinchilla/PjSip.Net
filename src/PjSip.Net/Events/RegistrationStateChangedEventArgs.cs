using PjSip.Net.Accounts;

namespace PjSip.Net.Events;

public sealed class RegistrationStateChangedEventArgs : SipEventArgs
{
    public required ISipAccount Account { get; init; }
    public required SipRegistrationState OldState { get; init; }
    public required SipRegistrationState NewState { get; init; }
    public int StatusCode { get; init; }
    public string? Reason { get; init; }
}
