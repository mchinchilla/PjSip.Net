namespace PjSip.Net.Media;

public interface ISipCodecManager
{
    IReadOnlyList<CodecInfo> GetCodecs();
    void SetCodecPriority(string codecId, int priority);
    void DisableCodec(string codecId);
    void EnableCodec(string codecId, int priority = 128);
}
