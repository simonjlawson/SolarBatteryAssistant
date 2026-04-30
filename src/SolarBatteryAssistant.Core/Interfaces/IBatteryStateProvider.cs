using SolarBatteryAssistant.Core.Models;

namespace SolarBatteryAssistant.Core.Interfaces;

/// <summary>
/// Provides the current battery state from HomeAssistant.
/// </summary>
public interface IBatteryStateProvider
{
    /// <summary>
    /// Gets the current battery state of charge.
    /// </summary>
    Task<BatteryState> GetCurrentStateAsync(CancellationToken cancellationToken = default);
}
