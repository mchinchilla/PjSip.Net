namespace PjSip.Net.Tones;

public interface ISipToneGenerator : IDisposable
{
    bool IsPlaying { get; }
    void PlayTone(ToneType tone);
    void PlayTones(IEnumerable<ToneDescriptor> tones);
    void PlayRingbackTone();
    void PlayBusyTone();
    void PlayDialTone();
    void Stop();
}
