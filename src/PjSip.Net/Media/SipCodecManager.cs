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

    private PjSipThreadSafeInvoker Invoker => _endpointManager.Invoker;

    public IReadOnlyList<CodecInfo> GetCodecs()
    {
        var ep = _endpointManager.Endpoint
            ?? throw new InvalidOperationException("Cannot enumerate codecs: SIP endpoint is not initialized. Call StartAsync() first.");

        return Invoker.InvokeAsync(() =>
        {
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

            return (IReadOnlyList<CodecInfo>)result;
        }).GetAwaiter().GetResult();
    }

    public void SetCodecPriority(string codecId, int priority)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codecId);

        var ep = _endpointManager.Endpoint
            ?? throw new InvalidOperationException("Cannot set codec priority: SIP endpoint is not initialized. Call StartAsync() first.");

        _logger.LogInformation("Setting codec {CodecId} priority to {Priority}", codecId, priority);
        Invoker.Invoke(() =>
        {
            try
            {
                ep.codecSetPriority(codecId, (byte)Math.Clamp(priority, 0, 255));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set codec {CodecId} priority", codecId);
            }
        });
    }

    public void DisableCodec(string codecId) => SetCodecPriority(codecId, 0);

    public void EnableCodec(string codecId, int priority = 128) => SetCodecPriority(codecId, priority);
}
