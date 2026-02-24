using Microsoft.Extensions.Logging;
using PjSip.Net.Internal;

namespace PjSip.Net.Media;

internal sealed class SipAudioManager : ISipAudioManager
{
    private readonly PjSipEndpointManager _endpointManager;
    private readonly ILogger _logger;
    private IReadOnlyList<AudioDeviceInfo>? _cachedInputDevices;
    private IReadOnlyList<AudioDeviceInfo>? _cachedOutputDevices;
    private float _inputLevel = 1.0f;
    private float _outputLevel = 1.0f;

    public SipAudioManager(PjSipEndpointManager endpointManager, ILogger logger)
    {
        _endpointManager = endpointManager;
        _logger = logger;
    }

    private PjSipThreadSafeInvoker Invoker => _endpointManager.Invoker;

    public AudioDeviceInfo? CurrentInputDevice { get; private set; }
    public AudioDeviceInfo? CurrentOutputDevice { get; private set; }

    public float InputLevel
    {
        get => _inputLevel;
        set
        {
            _inputLevel = Math.Clamp(value, 0f, 1.0f);
            ApplyInputLevel(_inputLevel);
        }
    }

    public float OutputLevel
    {
        get => _outputLevel;
        set
        {
            _outputLevel = Math.Clamp(value, 0f, 1.0f);
            ApplyOutputLevel(_outputLevel);
        }
    }

    public IReadOnlyList<AudioDeviceInfo> GetInputDevices()
    {
        if (_cachedInputDevices is not null) return _cachedInputDevices;
        _cachedInputDevices = EnumerateDevices(input: true);
        return _cachedInputDevices;
    }

    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices()
    {
        if (_cachedOutputDevices is not null) return _cachedOutputDevices;
        _cachedOutputDevices = EnumerateDevices(input: false);
        return _cachedOutputDevices;
    }

    /// <summary>
    /// Invalidates the cached device lists so the next call re-enumerates.
    /// </summary>
    public void RefreshDevices()
    {
        _cachedInputDevices = null;
        _cachedOutputDevices = null;
    }

    public void SetInputDevice(int deviceId)
    {
        // On iOS, audio device is managed via null device + on-demand open in
        // OnNativeMediaState. Calling setCaptureDev() outside of an active call
        // breaks the null device setup and causes makeCall to fail.
        if (OperatingSystem.IsIOS() && !OperatingSystem.IsMacCatalyst())
        {
            _logger.LogDebug("iOS: skipping SetInputDevice({DeviceId}) — managed via null device", deviceId);
            CurrentInputDevice = new AudioDeviceInfo { DeviceId = deviceId, Name = "iPhone IO device" };
            return;
        }

        var ep = _endpointManager.Endpoint;
        if (ep is not null)
        {
            Invoker.Invoke(() =>
            {
                try
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
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to set capture device {DeviceId}", deviceId);
                }
            });
        }
        else
        {
            CurrentInputDevice = new AudioDeviceInfo { DeviceId = deviceId, Name = $"Device {deviceId}" };
        }
    }

    public bool SetInputDeviceByName(string deviceName)
    {
        if (string.IsNullOrEmpty(deviceName)) return false;

        var devices = GetInputDevices();
        // Exact match first (case-insensitive)
        var match = devices.FirstOrDefault(d =>
            string.Equals(d.Name, deviceName, StringComparison.OrdinalIgnoreCase));
        // Fallback to contains match
        match ??= devices.FirstOrDefault(d =>
            d.Name.Contains(deviceName, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            _logger.LogWarning("SetInputDeviceByName: no device matching '{DeviceName}' found", deviceName);
            return false;
        }

        _logger.LogInformation("Resolved input device '{DeviceName}' → ID {DeviceId} ('{ActualName}')",
            deviceName, match.DeviceId, match.Name);
        SetInputDevice(match.DeviceId);
        return true;
    }

    public bool SetOutputDeviceByName(string deviceName)
    {
        if (string.IsNullOrEmpty(deviceName)) return false;

        var devices = GetOutputDevices();
        // Exact match first (case-insensitive)
        var match = devices.FirstOrDefault(d =>
            string.Equals(d.Name, deviceName, StringComparison.OrdinalIgnoreCase));
        // Fallback to contains match
        match ??= devices.FirstOrDefault(d =>
            d.Name.Contains(deviceName, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            _logger.LogWarning("SetOutputDeviceByName: no device matching '{DeviceName}' found", deviceName);
            return false;
        }

        _logger.LogInformation("Resolved output device '{DeviceName}' → ID {DeviceId} ('{ActualName}')",
            deviceName, match.DeviceId, match.Name);
        SetOutputDevice(match.DeviceId);
        return true;
    }

    public void SetOutputDevice(int deviceId)
    {
        // On iOS, audio device is managed via null device + on-demand open in
        // OnNativeMediaState. Calling setPlaybackDev() outside of an active call
        // breaks the null device setup and causes makeCall to fail.
        if (OperatingSystem.IsIOS() && !OperatingSystem.IsMacCatalyst())
        {
            _logger.LogDebug("iOS: skipping SetOutputDevice({DeviceId}) — managed via null device", deviceId);
            CurrentOutputDevice = new AudioDeviceInfo { DeviceId = deviceId, Name = "iPhone IO device" };
            return;
        }

        var ep = _endpointManager.Endpoint;
        if (ep is not null)
        {
            Invoker.Invoke(() =>
            {
                try
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
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to set playback device {DeviceId}", deviceId);
                }
            });
        }
        else
        {
            CurrentOutputDevice = new AudioDeviceInfo { DeviceId = deviceId, Name = $"Device {deviceId}" };
        }
    }

    private IReadOnlyList<AudioDeviceInfo> EnumerateDevices(bool input)
    {
        var ep = _endpointManager.Endpoint;
        if (ep is null)
        {
            _logger.LogDebug("EnumerateDevices({Input}): endpoint is null, returning empty", input ? "input" : "output");
            return [];
        }

        return Invoker.InvokeAsync(() =>
        {
            var devices = new List<AudioDeviceInfo>();
            var audMgr = ep.audDevManager();
            var devList = audMgr.enumDev2();

            _logger.LogDebug("EnumerateDevices({Input}): enumDev2 returned {Count} device(s)", input ? "input" : "output", devList.Count);

            for (int i = 0; i < devList.Count; i++)
            {
                var dev = devList[i];
                _logger.LogDebug("  Device[{Index}]: name={Name}, driver={Driver}, in={InputCount}, out={OutputCount}",
                    i, dev.name, dev.driver, dev.inputCount, dev.outputCount);
                bool matches = input ? dev.inputCount > 0 : dev.outputCount > 0;
                if (matches)
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

            _logger.LogDebug("EnumerateDevices({Input}): returning {Count} matching device(s)", input ? "input" : "output", devices.Count);
            return (IReadOnlyList<AudioDeviceInfo>)devices;
        }).GetAwaiter().GetResult();
    }

    private void ApplyInputLevel(float level)
    {
        var ep = _endpointManager.Endpoint;
        if (ep is null) return;

        Invoker.Invoke(() =>
        {
            try
            {
                var capMedia = ep.audDevManager().getCaptureDevMedia();
                capMedia.adjustRxLevel(level);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to adjust input level — no active media or endpoint not ready");
            }
        });
    }

    private void ApplyOutputLevel(float level)
    {
        var ep = _endpointManager.Endpoint;
        if (ep is null) return;

        Invoker.Invoke(() =>
        {
            try
            {
                var playMedia = ep.audDevManager().getPlaybackDevMedia();
                playMedia.adjustRxLevel(level);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to adjust output level — no active media or endpoint not ready");
            }
        });
    }
}
