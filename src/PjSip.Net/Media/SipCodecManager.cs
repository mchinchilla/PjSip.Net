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
        // TODO: When SWIG-generated classes are available:
        // 1. Call ep.codecEnum2() to get codec list
        // 2. Map to CodecInfo
        _logger.LogDebug("Getting codec list");
        return [];
    }

    public void SetCodecPriority(string codecId, int priority)
    {
        _logger.LogInformation("Setting codec {CodecId} priority to {Priority}", codecId, priority);

        // TODO: When SWIG-generated classes are available:
        // ep.codecSetPriority(codecId, priority)
    }

    public void DisableCodec(string codecId) => SetCodecPriority(codecId, 0);

    public void EnableCodec(string codecId, int priority = 128) => SetCodecPriority(codecId, priority);
}
