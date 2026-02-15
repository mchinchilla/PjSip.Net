namespace PjSip.Net.Media;

internal sealed class SipAudioManager : ISipAudioManager
{
    public AudioDeviceInfo? CurrentInputDevice { get; private set; }
    public AudioDeviceInfo? CurrentOutputDevice { get; private set; }
    public float InputLevel { get; set; } = 1.0f;
    public float OutputLevel { get; set; } = 1.0f;

    public IReadOnlyList<AudioDeviceInfo> GetInputDevices()
    {
        // TODO: When SWIG-generated classes are available:
        // Enumerate via pj.Endpoint.instance().audDevManager()
        return [];
    }

    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices()
    {
        // TODO: When SWIG-generated classes are available:
        // Enumerate via pj.Endpoint.instance().audDevManager()
        return [];
    }

    public void SetInputDevice(int deviceId)
    {
        // TODO: pj.Endpoint.instance().audDevManager().setCaptureDev(deviceId)
        CurrentInputDevice = new AudioDeviceInfo { DeviceId = deviceId, Name = $"Device {deviceId}" };
    }

    public void SetOutputDevice(int deviceId)
    {
        // TODO: pj.Endpoint.instance().audDevManager().setPlaybackDev(deviceId)
        CurrentOutputDevice = new AudioDeviceInfo { DeviceId = deviceId, Name = $"Device {deviceId}" };
    }
}
