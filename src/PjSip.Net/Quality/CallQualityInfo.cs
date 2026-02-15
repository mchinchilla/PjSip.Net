namespace PjSip.Net.Quality;

public sealed record CallQualityInfo
{
    public required string CallId { get; init; }
    public TimeSpan Duration { get; init; }

    // RTP Statistics
    public long RtpPacketsSent { get; init; }
    public long RtpPacketsReceived { get; init; }
    public long RtpPacketsLost { get; init; }
    public double RtpLossPercentage { get; init; }
    public int RtpJitterMs { get; init; }
    public int RtpRoundTripTimeMs { get; init; }

    // Codec info
    public string? CodecName { get; init; }
    public int CodecClockRate { get; init; }

    // MOS score (estimated)
    public double MosScore { get; init; }
}
