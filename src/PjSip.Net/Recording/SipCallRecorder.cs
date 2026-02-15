using Microsoft.Extensions.Logging;
using PjSip.Net.Calls;
using PjSip.Net.Internal;
using PjSip.Net.Interop.Generated;

namespace PjSip.Net.Recording;

internal sealed class SipCallRecorder : ISipCallRecorder
{
    private readonly PjSipEndpointManager _endpointManager;
    private readonly ILogger _logger;
    private readonly Lock _lock = new();
    private volatile bool _disposed;
    private ISipCall? _recordingCall;
    private AudioMediaRecorder? _recorder;
    private volatile bool _isRecording;

    public SipCallRecorder(
        PjSipEndpointManager endpointManager,
        ILogger logger)
    {
        _endpointManager = endpointManager;
        _logger = logger;
    }

    public bool IsRecording => _isRecording;
    public string? CurrentFilePath { get; private set; }

    public event EventHandler<RecordingStateChangedEventArgs>? RecordingStateChanged;

    public void StartRecording(ISipCall call, string filePath, RecordingFormat format = RecordingFormat.Wav)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lock)
        {
            if (_isRecording) throw new InvalidOperationException("Already recording.");

            _recordingCall = call;
            CurrentFilePath = filePath;

            if (call is ManagedCall managed && managed.AudioMedia is not null)
            {
                _endpointManager.Invoker.Invoke(() =>
                {
                    try
                    {
                        _recorder = new AudioMediaRecorder();
                        _recorder.createRecorder(filePath);
                        managed.AudioMedia.startTransmit(_recorder);
                        _isRecording = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error starting native recording for call {CallId}", call.Id);
                        _recorder?.Dispose();
                        _recorder = null;
                        _recordingCall = null;
                        CurrentFilePath = null;
                        return;
                    }
                });
            }
            else
            {
                _isRecording = true;
            }
        }

        if (_isRecording)
        {
            _logger.LogInformation("Started recording call {CallId} to {FilePath}", call.Id, filePath);
            RecordingStateChanged?.Invoke(this, new RecordingStateChangedEventArgs
            {
                Call = call,
                IsRecording = true,
                FilePath = filePath
            });
        }
    }

    public void StopRecording()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ISipCall? call;
        string? filePath;

        lock (_lock)
        {
            if (!_isRecording) return;

            call = _recordingCall;
            filePath = CurrentFilePath;

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

            _isRecording = false;
            CurrentFilePath = null;
            _recordingCall = null;
        }

        _logger.LogInformation("Stopped recording call {CallId}", call?.Id);

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
        if (_isRecording) StopRecording();
    }
}
