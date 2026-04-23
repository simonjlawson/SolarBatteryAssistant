using SolarBatteryAssistant.Core.Models;

namespace SolarBatteryAssistant.Core.Interfaces;

/// <summary>
/// Plans and optimises an energy management strategy for the day.
/// </summary>
public interface IEnergyPlanner
{
    /// <summary>
    /// Generates a full day's energy plan based on prices, solar forecast, and current battery state.
    /// </summary>
    Task<EnergyPlan> GeneratePlanAsync(
        DateOnly planDate,
        IReadOnlyList<EnergyPrice> prices,
        SolarForecast solarForecast,
        BatteryState currentBatteryState,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-evaluates the remaining slots of an existing plan given the current battery state.
    /// Returns a revised plan if changes are warranted.
    /// </summary>
    Task<EnergyPlan> ReEvaluatePlanAsync(
        EnergyPlan currentPlan,
        BatteryState currentBatteryState,
        CancellationToken cancellationToken = default);
}
