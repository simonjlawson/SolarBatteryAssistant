using Microsoft.Extensions.DependencyInjection;
using SolarBatteryAssistant.Core.Interfaces;
using SolarBatteryAssistant.Infrastructure.HomeAssistant;
using SolarBatteryAssistant.Infrastructure.Providers;

namespace SolarBatteryAssistant.Infrastructure;

/// <summary>
/// DI registration for infrastructure services (HomeAssistant client, price/solar providers).
/// </summary>
public static class InfrastructureServiceExtensions
{
    /// <summary>
    /// Registers all infrastructure implementations:
    /// - HomeAssistant API client
    /// - Octopus Agile price provider
    /// - HomeAssistant battery state provider
    /// - HomeAssistant solar forecast provider
    /// - HomeAssistant scene controller
    /// </summary>
    public static IServiceCollection AddSolarBatteryInfrastructure(this IServiceCollection services)
    {
        services.AddHttpClient<HomeAssistantClient>();
        services.AddHttpClient<OctopusAgilePriceProvider>();

        services.AddSingleton<IEnergyPriceProvider, OctopusAgilePriceProvider>();
        services.AddSingleton<ISolarForecastProvider, HomeAssistantSolarForecastProvider>();
        services.AddSingleton<IBatteryStateProvider, HomeAssistantBatteryStateProvider>();
        services.AddSingleton<ISceneController, HomeAssistantSceneController>();

        return services;
    }
}
