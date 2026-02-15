namespace PjSip.Net.Media;

public interface ISipAudioManager
{
    IReadOnlyList<AudioDeviceInfo> GetInputDevices();
    IReadOnlyList<AudioDeviceInfo> GetOutputDevices();
    AudioDeviceInfo? CurrentInputDevice { get; }
    AudioDeviceInfo? CurrentOutputDevice { get; }
    void SetInputDevice(int deviceId);
    void SetOutputDevice(int deviceId);
    float InputLevel { get; set; }
    float OutputLevel { get; set; }
}
