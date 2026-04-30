using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SolarBatteryAssistant.Core.Configuration;
using SolarBatteryAssistant.Core.Interfaces;
using SolarBatteryAssistant.Core.Planning;
using SolarBatteryAssistant.Core.Storage;

namespace SolarBatteryAssistant.Core;

/// <summary>
/// Dependency injection extensions for core services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the core SolarBatteryAssistant services (planner, repository, configuration).
    /// Daemon/Simulator projects then add their own provider implementations.
    /// </summary>
    public static IServiceCollection AddSolarBatteryAssistantCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DaemonConfiguration>(
            configuration.GetSection(DaemonConfiguration.SectionName));

        services.AddSingleton<IEnergyPlanner, EnergyPlanner>();
        services.AddSingleton<IPlanRepository, JsonPlanRepository>();

        return services;
    }
}
