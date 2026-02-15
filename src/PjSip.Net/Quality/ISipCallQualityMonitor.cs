using PjSip.Net.Calls;

namespace PjSip.Net.Quality;

public interface ISipCallQualityMonitor
{
    CallQualityInfo? GetQuality(ISipCall call);
    Task<CallQualityInfo?> GetQualityAsync(ISipCall call, CancellationToken ct = default);
    event EventHandler<CallQualityEventArgs>? QualityReportAvailable;
}
