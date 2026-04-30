using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SolarBatteryAssistant.Core.Configuration;
using SolarBatteryAssistant.Core.Interfaces;
using SolarBatteryAssistant.Core.Models;
using SolarBatteryAssistant.Infrastructure.HomeAssistant;

namespace SolarBatteryAssistant.Infrastructure.Providers;

/// <summary>
/// Reads the solar generation forecast from a HomeAssistant entity.
/// The entity should contain today's total predicted generation in Wh.
/// </summary>
public class HomeAssistantSolarForecastProvider : ISolarForecastProvider
{
    private readonly HomeAssistantClient _haClient;
    private readonly SolarConfiguration _config;
    private readonly ILogger<HomeAssistantSolarForecastProvider> _logger;

    // Cache to avoid re-fetching the same day's forecast multiple times
    private SolarForecast? _cachedForecast;

    public HomeAssistantSolarForecastProvider(
        HomeAssistantClient haClient,
        IOptions<DaemonConfiguration> config,
        ILogger<HomeAssistantSolarForecastProvider> logger)
    {
        _haClient = haClient;
        _config = config.Value.Solar;
        _logger = logger;
    }

    public async Task<SolarForecast?> GetForecastAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        // Return cached value if it's for the same date
        if (_cachedForecast != null && _cachedForecast.ForecastDate == date)
            return _cachedForecast;

        var entityState = await _haClient.GetEntityStateAsync(_config.ForecastEntityId, cancellationToken);

        if (entityState == null)
        {
            _logger.LogWarning("Could not read solar forecast entity {Entity}.", _config.ForecastEntityId);
            return FallbackForecast(date);
        }

        if (!double.TryParse(entityState.State, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double forecastWh))
        {
            _logger.LogWarning("Solar forecast entity {Entity} has non-numeric state '{State}'.",
                _config.ForecastEntityId, entityState.State);
            return FallbackForecast(date);
        }

        _logger.LogInformation("Solar forecast for {Date}: {Raw}Wh (scale={Scale}, effective={Effective}Wh)",
            date, forecastWh, _config.ScaleFactor, forecastWh * _config.ScaleFactor);

        _cachedForecast = new SolarForecast
        {
            ForecastDate = date,
            RawForecastWatts = forecastWh,
            ScaleFactor = _config.ScaleFactor,
            ReceivedAt = entityState.LastUpdated
        };

        return _cachedForecast;
    }

    private SolarForecast FallbackForecast(DateOnly date)
    {
        _logger.LogWarning("Using zero solar forecast as fallback for {Date}.", date);
        return new SolarForecast
        {
            ForecastDate = date,
            RawForecastWatts = 0,
            ScaleFactor = _config.ScaleFactor,
            ReceivedAt = DateTimeOffset.UtcNow
        };
    }
}
