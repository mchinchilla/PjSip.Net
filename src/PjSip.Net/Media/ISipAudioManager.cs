namespace PjSip.Net.Media;

public interface ISipAudioManager
{
    IReadOnlyList<AudioDeviceInfo> GetInputDevices();
    IReadOnlyList<AudioDeviceInfo> GetOutputDevices();
    AudioDeviceInfo? CurrentInputDevice { get; }
    AudioDeviceInfo? CurrentOutputDevice { get; }
    void SetInputDevice(int deviceId);
    void SetOutputDevice(int deviceId);
    bool SetInputDeviceByName(string deviceName);
    bool SetOutputDeviceByName(string deviceName);
    float InputLevel { get; set; }
    float OutputLevel { get; set; }

    /// <summary>
    /// Invalidates cached device lists so the next enumeration re-reads from the OS.
    /// </summary>
    void RefreshDevices();

    /// <summary>
    /// Raised when the platform detects an audio route change (e.g. Bluetooth
    /// connected/disconnected, headset plugged, CarPlay, Android Auto).
    /// </summary>
    event EventHandler<AudioRouteChangedEventArgs>? AudioRouteChanged;

    /// <summary>
    /// Notifies that an audio route change occurred. Call this from platform-specific
    /// listeners (AVAudioSession on iOS, AudioDeviceCallback on Android).
    /// </summary>
    void NotifyAudioRouteChanged(string reason, string? newDeviceName = null);
}
