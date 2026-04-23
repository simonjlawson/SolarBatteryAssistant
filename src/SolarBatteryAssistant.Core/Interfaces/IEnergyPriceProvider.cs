using SolarBatteryAssistant.Core.Models;

namespace SolarBatteryAssistant.Core.Interfaces;

/// <summary>
/// Provides energy import and export prices for 30-minute slots.
/// </summary>
public interface IEnergyPriceProvider
{
    /// <summary>
    /// Gets the energy prices for all available 30-minute slots on the given date.
    /// </summary>
    Task<IReadOnlyList<EnergyPrice>> GetPricesForDateAsync(DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the energy price for the current 30-minute slot.
    /// </summary>
    Task<EnergyPrice?> GetCurrentPriceAsync(CancellationToken cancellationToken = default);
}
