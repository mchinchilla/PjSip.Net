using PjSip.Net.Calls;

namespace PjSip.Net.Recording;

public interface ISipCallRecorder : IDisposable
{
    bool IsRecording { get; }
    string? CurrentFilePath { get; }
    void StartRecording(ISipCall call, string filePath, RecordingFormat format = RecordingFormat.Wav);
    void StopRecording();
    event EventHandler<RecordingStateChangedEventArgs>? RecordingStateChanged;
}
