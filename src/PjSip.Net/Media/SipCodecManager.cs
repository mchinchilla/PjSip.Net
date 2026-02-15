using Microsoft.Extensions.Logging;
using PjSip.Net.Internal;

namespace PjSip.Net.Media;

internal sealed class SipCodecManager : ISipCodecManager
{
    private readonly PjSipEndpointManager _endpointManager;
    private readonly ILogger _logger;

    public SipCodecManager(
        PjSipEndpointManager endpointManager,
        ILogger logger)
    {
        _endpointManager = endpointManager;
        _logger = logger;
    }

    public IReadOnlyList<CodecInfo> GetCodecs()
    {
        var ep = _endpointManager.Endpoint;
        if (ep is null)
        {
            _logger.LogDebug("Getting codec list (no native endpoint)");
            return [];
        }

        var codecList = ep.codecEnum2();
        var result = new List<CodecInfo>(codecList.Count);

        for (int i = 0; i < codecList.Count; i++)
        {
            var c = codecList[i];
            result.Add(new CodecInfo
            {
                CodecId = c.codecId,
                Description = c.desc,
                Priority = c.priority
            });
        }

        return result;
    }

    public void SetCodecPriority(string codecId, int priority)
    {
        _logger.LogInformation("Setting codec {CodecId} priority to {Priority}", codecId, priority);

        var ep = _endpointManager.Endpoint;
        ep?.codecSetPriority(codecId, (byte)Math.Clamp(priority, 0, 255));
    }

    public void DisableCodec(string codecId) => SetCodecPriority(codecId, 0);

    public void EnableCodec(string codecId, int priority = 128) => SetCodecPriority(codecId, priority);
}
