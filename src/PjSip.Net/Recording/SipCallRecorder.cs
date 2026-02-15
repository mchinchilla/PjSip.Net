using Microsoft.Extensions.Logging;
using PjSip.Net.Calls;
using PjSip.Net.Internal;

namespace PjSip.Net.Recording;

internal sealed class SipCallRecorder : ISipCallRecorder
{
    private readonly PjSipEndpointManager _endpointManager;
    private readonly ILogger _logger;
    private volatile bool _disposed;
    private ISipCall? _recordingCall;

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

        // TODO: When SWIG-generated classes are available:
        // 1. Create AudioMediaRecorder
        // 2. recorder.createRecorder(filePath)
        // 3. Get call's AudioMedia
        // 4. audioMedia.startTransmit(recorder)

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

        // TODO: When SWIG-generated classes are available:
        // 1. Stop transmit to recorder
        // 2. Destroy recorder

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
