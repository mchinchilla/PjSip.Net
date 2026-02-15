using Microsoft.Extensions.Logging;
using PjSip.Net.Internal;

namespace PjSip.Net.Tones;

internal sealed class SipToneGenerator : ISipToneGenerator
{
    private readonly PjSipEndpointManager _endpointManager;
    private readonly ILogger _logger;
    private volatile bool _disposed;

    public SipToneGenerator(
        PjSipEndpointManager endpointManager,
        ILogger logger)
    {
        _endpointManager = endpointManager;
        _logger = logger;
    }

    public bool IsPlaying { get; private set; }

    public void PlayTone(ToneType tone)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _logger.LogDebug("Playing tone {ToneType}", tone);

        switch (tone)
        {
            case ToneType.Ringback: PlayRingbackTone(); break;
            case ToneType.Busy: PlayBusyTone(); break;
            case ToneType.Dial: PlayDialTone(); break;
        }
    }

    public void PlayTones(IEnumerable<ToneDescriptor> tones)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IsPlaying = true;

        // TODO: When SWIG-generated classes are available:
        // 1. Create ToneGenerator
        // 2. For each descriptor, create ToneDesc with freq/on/off
        // 3. Call toneGen.createToneGenerator()
        // 4. toneGen.play(tones)
        // 5. Connect to sound device
    }

    public void PlayRingbackTone()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _logger.LogDebug("Playing ringback tone");
        IsPlaying = true;
        PlayTones([new() { Frequency1 = 440, Frequency2 = 480, OnMs = 2000, OffMs = 4000 }]);
    }

    public void PlayBusyTone()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _logger.LogDebug("Playing busy tone");
        IsPlaying = true;
        PlayTones([new() { Frequency1 = 480, Frequency2 = 620, OnMs = 500, OffMs = 500 }]);
    }

    public void PlayDialTone()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _logger.LogDebug("Playing dial tone");
        IsPlaying = true;
        PlayTones([new() { Frequency1 = 350, Frequency2 = 440, OnMs = -1, OffMs = 0 }]);
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IsPlaying = false;
        _logger.LogDebug("Stopped tone playback");

        // TODO: When SWIG-generated classes are available:
        // 1. Stop the tone generator
        // 2. Disconnect from sound device
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (IsPlaying) Stop();
    }
}
