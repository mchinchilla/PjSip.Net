using PjSip.Net.Calls;
using PjSip.Net.Events;

namespace PjSip.Net.Recording;

public sealed class RecordingStateChangedEventArgs : SipEventArgs
{
    public required ISipCall Call { get; init; }
    public required bool IsRecording { get; init; }
    public string? FilePath { get; init; }
}
