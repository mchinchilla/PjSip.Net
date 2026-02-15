using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PjSip.Net.Conference;
using PjSip.Net.DependencyInjection;
using PjSip.Net.History;
using PjSip.Net.Media;
using PjSip.Net.Messaging;
using PjSip.Net.Network;
using PjSip.Net.Presence;
using PjSip.Net.Quality;
using PjSip.Net.Recording;
using PjSip.Net.Tones;
using PjSip.Net.Transport;

namespace PjSip.Net.Tests.Unit;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPjSip_ShouldRegisterISipPhone()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPjSip(options =>
        {
            options.UserAgent = "TestAgent";
        });

        var provider = services.BuildServiceProvider();
        var phone = provider.GetService<ISipPhone>();
        phone.Should().NotBeNull();
    }

    [Fact]
    public void AddPjSip_ShouldRegisterISipAudioManager()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPjSip(options => { });

        var provider = services.BuildServiceProvider();
        var audio = provider.GetService<ISipAudioManager>();
        audio.Should().NotBeNull();
    }

    [Fact]
    public void AddPjSip_Singleton_ShouldReturnSameInstance()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPjSip(options => { }, PjSipServiceLifetime.Singleton);

        var provider = services.BuildServiceProvider();
        var phone1 = provider.GetRequiredService<ISipPhone>();
        var phone2 = provider.GetRequiredService<ISipPhone>();
        phone1.Should().BeSameAs(phone2);
    }

    [Fact]
    public void AddPjSip_WithTransportConfig_ShouldApplyOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPjSip(options =>
        {
            options.Transports.Add(new SipTransportOptions
            {
                Type = SipTransportType.Tls,
                Port = 5061
            });
        });

        var provider = services.BuildServiceProvider();
        var phone = provider.GetRequiredService<ISipPhone>();
        phone.Should().NotBeNull();
    }

    [Fact]
    public void AddPjSip_ShouldRegisterAllSubServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPjSip(options => { });
        var provider = services.BuildServiceProvider();

        provider.GetService<ISipCodecManager>().Should().NotBeNull();
        provider.GetService<ISipPresenceManager>().Should().NotBeNull();
        provider.GetService<ISipMessaging>().Should().NotBeNull();
        provider.GetService<ISipConferenceBridge>().Should().NotBeNull();
        provider.GetService<ISipCallRecorder>().Should().NotBeNull();
        provider.GetService<ISipToneGenerator>().Should().NotBeNull();
        provider.GetService<ISipCallQualityMonitor>().Should().NotBeNull();
        provider.GetService<ISipCallHistory>().Should().NotBeNull();
        provider.GetService<ISipNetworkMonitor>().Should().NotBeNull();
    }
}
