namespace PjSip.Net.Media;

public sealed class AudioRouteChangedEventArgs : EventArgs
{
    /// <summary>
    /// Platform-specific reason for the route change (e.g. "NewDeviceAvailable",
    /// "OldDeviceUnavailable", "Override", "CategoryChange").
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Name of the new audio device, if known.
    /// </summary>
    public string? NewDeviceName { get; init; }
}
