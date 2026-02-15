using Microsoft.Extensions.Logging;
using PjSip.Net.Calls;
using PjSip.Net.Internal;
using PjSip.Net.Interop.Generated;

namespace PjSip.Net.Recording;

internal sealed class SipCallRecorder : ISipCallRecorder
{
    private readonly PjSipEndpointManager _endpointManager;
    private readonly ILogger _logger;
    private volatile bool _disposed;
    private ISipCall? _recordingCall;
    private AudioMediaRecorder? _recorder;

    public SipCallRecorder(
        PjSipEndpointManager endpointManager,
        ILogger logger)
    {
        _endpointManager = endpointManager;
        _logger = logger;
    }

    public bool IsRecording { get; private set; }
    public string? CurrentFilePath { get; private set; }

    public event EventHandler<RecordingStateChangedEventArgs>? RecordingStateChanged;

    public void StartRecording(ISipCall call, string filePath, RecordingFormat format = RecordingFormat.Wav)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsRecording) throw new InvalidOperationException("Already recording.");

        _recordingCall = call;
        CurrentFilePath = filePath;
        IsRecording = true;

        _logger.LogInformation("Started recording call {CallId} to {FilePath}", call.Id, filePath);

        if (call is ManagedCall managed && managed.AudioMedia is not null)
        {
            _endpointManager.Invoker.Invoke(() =>
            {
                try
                {
                    _recorder = new AudioMediaRecorder();
                    _recorder.createRecorder(filePath);
                    managed.AudioMedia.startTransmit(_recorder);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error starting native recording for call {CallId}", call.Id);
                    _recorder?.Dispose();
                    _recorder = null;
                }
            });
        }

        RecordingStateChanged?.Invoke(this, new RecordingStateChangedEventArgs
        {
            Call = call,
            IsRecording = true,
            FilePath = filePath
        });
    }

    public void StopRecording()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsRecording) return;

        _logger.LogInformation("Stopped recording call {CallId}", _recordingCall?.Id);

        if (_recorder is not null && _recordingCall is ManagedCall managed && managed.AudioMedia is not null)
        {
            _endpointManager.Invoker.Invoke(() =>
            {
                try
                {
                    managed.AudioMedia.stopTransmit(_recorder);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error stopping native recording");
                }
                _recorder.Dispose();
                _recorder = null;
            });
        }

        var call = _recordingCall;
        var filePath = CurrentFilePath;
        IsRecording = false;
        CurrentFilePath = null;
        _recordingCall = null;

        if (call is not null)
        {
            RecordingStateChanged?.Invoke(this, new RecordingStateChangedEventArgs
            {
                Call = call,
                IsRecording = false,
                FilePath = filePath
            });
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (IsRecording) StopRecording();
    }
}
