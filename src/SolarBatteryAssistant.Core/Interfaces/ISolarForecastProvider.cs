using SolarBatteryAssistant.Core.Models;

namespace SolarBatteryAssistant.Core.Interfaces;

/// <summary>
/// Provides solar generation forecasts.
/// </summary>
public interface ISolarForecastProvider
{
    /// <summary>
    /// Gets the solar generation forecast for the given date.
    /// </summary>
    Task<SolarForecast?> GetForecastAsync(DateOnly date, CancellationToken cancellationToken = default);
}
