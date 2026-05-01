using SolarBatteryAssistant.Core.Models;

namespace SolarBatteryAssistant.Core.Interfaces;

/// <summary>
/// Persists and retrieves energy plans for audit and review purposes.
/// </summary>
public interface IPlanRepository
{
    /// <summary>Saves a plan (create or update).</summary>
    Task SavePlanAsync(EnergyPlan plan, CancellationToken cancellationToken = default);

    /// <summary>Gets the plan for a specific date. Returns null if none exists.</summary>
    Task<EnergyPlan?> GetPlanAsync(DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>Gets all stored plan dates in descending order.</summary>
    Task<IReadOnlyList<DateOnly>> GetAvailablePlanDatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all stored plan files and resets any related caches.
    /// </summary>
    Task ClearAllPlansAsync(CancellationToken cancellationToken = default);
}
