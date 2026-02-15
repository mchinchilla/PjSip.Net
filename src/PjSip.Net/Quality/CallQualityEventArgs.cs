using PjSip.Net.Calls;
using PjSip.Net.Events;

namespace PjSip.Net.Quality;

public sealed class CallQualityEventArgs : SipEventArgs
{
    public required ISipCall Call { get; init; }
    public required CallQualityInfo Quality { get; init; }
}
