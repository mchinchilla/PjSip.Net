using Microsoft.Extensions.Logging;
using PjSip.Net.Internal;
using PjSip.Net.Interop.Generated;

namespace PjSip.Net.Tones;

internal sealed class SipToneGenerator : ISipToneGenerator
{
    private readonly PjSipEndpointManager _endpointManager;
    private readonly ILogger _logger;
    private ToneGenerator? _toneGen;
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

    public void PlayTones(IEnumerable<ToneDescriptor> tones, bool loop = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Stop any currently playing tone first
        if (IsPlaying) Stop();

        IsPlaying = true;

        var ep = _endpointManager.Endpoint;
        if (ep is null) return;

        _endpointManager.Invoker.Invoke(() =>
        {
            try
            {
                // Clean up previous tone generator (defensive)
                _toneGen?.Dispose();

                _toneGen = new ToneGenerator();
                _toneGen.createToneGenerator();

                using var toneDescVec = new ToneDescVector();
                foreach (var desc in tones)
                {
                    var td = new ToneDesc
                    {
                        freq1 = (short)desc.Frequency1,
                        freq2 = (short)desc.Frequency2,
                        on_msec = (short)desc.OnMs,
                        off_msec = (short)desc.OffMs,
                        volume = (short)desc.Volume
                    };
                    toneDescVec.Add(td);
                }

                _toneGen.play(toneDescVec, loop);

                // Connect to playback device
                var playMedia = ep.audDevManager().getPlaybackDevMedia();
                _toneGen.startTransmit(playMedia);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error playing tones");
                _toneGen?.Dispose();
                _toneGen = null;
                IsPlaying = false;
            }
        });
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

        if (_toneGen is not null)
        {
            _endpointManager.Invoker.Invoke(() =>
            {
                // Snapshot on the invoker thread: concurrent/double Stop calls queue
                // multiple lambdas, and the first one nulls the field — the others
                // must see that and bail instead of dereferencing null.
                var toneGen = _toneGen;
                _toneGen = null;
                if (toneGen is null) return;

                try
                {
                    toneGen.stop();

                    var ep = _endpointManager.Endpoint;
                    var playMedia = ep?.audDevManager()?.getPlaybackDevMedia();
                    if (playMedia is not null)
                        toneGen.stopTransmit(playMedia);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error stopping tone generator");
                }
                toneGen.Dispose();
            });
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (IsPlaying) Stop();
    }
}
