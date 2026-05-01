using ImpSoft.OctopusEnergy.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SolarBatteryAssistant.Core.Configuration;
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
    /// - ImpSoft Octopus Energy client (for unit-rate fetching)
    /// - OctopusAccountService (account tariff discovery)
    /// - Octopus Agile price provider
    /// - HomeAssistant battery state provider
    /// - HomeAssistant solar forecast provider
    /// - HomeAssistant scene controller
    /// </summary>
    public static IServiceCollection AddSolarBatteryInfrastructure(this IServiceCollection services)
    {
        services.AddHttpClient<HomeAssistantClient>();

        // Named HttpClient for OctopusAccountService (needs Basic Auth; base address from config)
        services.AddHttpClient<OctopusAccountService>();

        // Register IOctopusEnergyClient / OctopusEnergyClient — base address from config;
        // standard-unit-rate endpoints are public so no auth header is needed.
        services.AddHttpClient<IOctopusEnergyClient, OctopusEnergyClient>((sp, http) =>
        {
            var config = sp.GetRequiredService<IOptions<DaemonConfiguration>>().Value
                           .EnergyPricing.Octopus;
            http.BaseAddress = new Uri(config.BaseUrl.TrimEnd('/') + "/");
        });

        services.AddSingleton<OctopusAccountService>();
        services.AddSingleton<IEnergyPriceProvider, OctopusAgilePriceProvider>();
        services.AddSingleton<ISolarForecastProvider, HomeAssistantSolarForecastProvider>();
        services.AddSingleton<IBatteryStateProvider, HomeAssistantBatteryStateProvider>();
        services.AddSingleton<ISceneController, HomeAssistantSceneController>();

        return services;
    }
}
