using PjSip.Net.Internal;

namespace PjSip.Net.Media;

internal sealed class SipAudioManager : ISipAudioManager
{
    private readonly PjSipEndpointManager _endpointManager;

    public SipAudioManager(PjSipEndpointManager endpointManager)
    {
        _endpointManager = endpointManager;
    }

    public AudioDeviceInfo? CurrentInputDevice { get; private set; }
    public AudioDeviceInfo? CurrentOutputDevice { get; private set; }
    public float InputLevel { get; set; } = 1.0f;
    public float OutputLevel { get; set; } = 1.0f;

    public IReadOnlyList<AudioDeviceInfo> GetInputDevices()
    {
        var ep = _endpointManager.Endpoint;
        if (ep is null) return [];

        var devices = new List<AudioDeviceInfo>();
        var audMgr = ep.audDevManager();
        var devList = audMgr.enumDev2();

        for (int i = 0; i < devList.Count; i++)
        {
            var dev = devList[i];
            if (dev.inputCount > 0)
            {
                devices.Add(new AudioDeviceInfo
                {
                    DeviceId = i,
                    Name = dev.name,
                    InputChannels = (int)dev.inputCount,
                    OutputChannels = (int)dev.outputCount,
                    Driver = dev.driver
                });
            }
        }

        return devices;
    }

    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices()
    {
        var ep = _endpointManager.Endpoint;
        if (ep is null) return [];

        var devices = new List<AudioDeviceInfo>();
        var audMgr = ep.audDevManager();
        var devList = audMgr.enumDev2();

        for (int i = 0; i < devList.Count; i++)
        {
            var dev = devList[i];
            if (dev.outputCount > 0)
            {
                devices.Add(new AudioDeviceInfo
                {
                    DeviceId = i,
                    Name = dev.name,
                    InputChannels = (int)dev.inputCount,
                    OutputChannels = (int)dev.outputCount,
                    Driver = dev.driver
                });
            }
        }

        return devices;
    }

    public void SetInputDevice(int deviceId)
    {
        var ep = _endpointManager.Endpoint;
        if (ep is not null)
        {
            ep.audDevManager().setCaptureDev(deviceId);
            var devInfo = ep.audDevManager().getDevInfo(deviceId);
            CurrentInputDevice = new AudioDeviceInfo
            {
                DeviceId = deviceId,
                Name = devInfo.name,
                InputChannels = (int)devInfo.inputCount,
                OutputChannels = (int)devInfo.outputCount,
                Driver = devInfo.driver
            };
        }
        else
        {
            CurrentInputDevice = new AudioDeviceInfo { DeviceId = deviceId, Name = $"Device {deviceId}" };
        }
    }

    public void SetOutputDevice(int deviceId)
    {
        var ep = _endpointManager.Endpoint;
        if (ep is not null)
        {
            ep.audDevManager().setPlaybackDev(deviceId);
            var devInfo = ep.audDevManager().getDevInfo(deviceId);
            CurrentOutputDevice = new AudioDeviceInfo
            {
                DeviceId = deviceId,
                Name = devInfo.name,
                InputChannels = (int)devInfo.inputCount,
                OutputChannels = (int)devInfo.outputCount,
                Driver = devInfo.driver
            };
        }
        else
        {
            CurrentOutputDevice = new AudioDeviceInfo { DeviceId = deviceId, Name = $"Device {deviceId}" };
        }
    }
}
