using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PjSip.Net.Conference;
using PjSip.Net.History;
using PjSip.Net.Internal;
using PjSip.Net.Logging;
using PjSip.Net.Media;
using PjSip.Net.Messaging;
using PjSip.Net.Network;
using PjSip.Net.Presence;
using PjSip.Net.Quality;
using PjSip.Net.Recording;
using PjSip.Net.Tones;

namespace PjSip.Net.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPjSip(
        this IServiceCollection services,
        Action<SipPhoneOptions> configure)
    {
        return services.AddPjSip(configure, PjSipServiceLifetime.Singleton);
    }

    public static IServiceCollection AddPjSip(
        this IServiceCollection services,
        Action<SipPhoneOptions> configure,
        PjSipServiceLifetime lifetime)
    {
        services.Configure(configure);

        services.TryAddSingleton<PjSipLoggerAdapter>();
        services.TryAddSingleton<PjSipEndpointManager>();

        switch (lifetime)
        {
            case PjSipServiceLifetime.Singleton:
                services.TryAddSingleton<ISipPhone, SipPhone>();
                RegisterSubServices(services, ServiceLifetime.Singleton);
                break;
            case PjSipServiceLifetime.Scoped:
                services.TryAddScoped<ISipPhone, SipPhone>();
                RegisterSubServices(services, ServiceLifetime.Scoped);
                break;
        }

        return services;
    }

    private static void RegisterSubServices(IServiceCollection services, ServiceLifetime lifetime)
    {
        Register<ISipAudioManager>(services, lifetime, sp => sp.GetRequiredService<ISipPhone>().Audio);
        Register<ISipCodecManager>(services, lifetime, sp => sp.GetRequiredService<ISipPhone>().Codecs);
        Register<ISipPresenceManager>(services, lifetime, sp => sp.GetRequiredService<ISipPhone>().Presence);
        Register<ISipMessaging>(services, lifetime, sp => sp.GetRequiredService<ISipPhone>().Messaging);
        Register<ISipConferenceBridge>(services, lifetime, sp => sp.GetRequiredService<ISipPhone>().Conference);
        Register<ISipCallRecorder>(services, lifetime, sp => sp.GetRequiredService<ISipPhone>().Recorder);
        Register<ISipToneGenerator>(services, lifetime, sp => sp.GetRequiredService<ISipPhone>().Tones);
        Register<ISipCallQualityMonitor>(services, lifetime, sp => sp.GetRequiredService<ISipPhone>().Quality);
        Register<ISipCallHistory>(services, lifetime, sp => sp.GetRequiredService<ISipPhone>().History);
        Register<ISipNetworkMonitor>(services, lifetime, sp => sp.GetRequiredService<ISipPhone>().Network);
    }

    private static void Register<TService>(
        IServiceCollection services,
        ServiceLifetime lifetime,
        Func<IServiceProvider, TService> factory) where TService : class
    {
        var descriptor = new ServiceDescriptor(typeof(TService), sp => factory(sp), lifetime);
        services.TryAdd(descriptor);
    }
}
