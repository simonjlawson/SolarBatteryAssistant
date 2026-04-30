namespace SolarBatteryAssistant.Core.Configuration;

/// <summary>
/// Solar generation forecast configuration.
/// </summary>
public class SolarConfiguration
{
    /// <summary>
    /// Provider mode for solar forecasting.
    /// </summary>
    public SolarForecastMode Mode { get; set; } = SolarForecastMode.HomeAssistantEntity;

    /// <summary>
    /// HomeAssistant entity_id that contains the total daily forecast in Wh.
    /// Used when Mode is HomeAssistantEntity.
    /// </summary>
    public string ForecastEntityId { get; set; } = "sensor.solar_forecast_today";

    /// <summary>
    /// Time of day (local) when the forecast entity is updated and should be read.
    /// Used when Mode is HomeAssistantEntity.
    /// </summary>
    public TimeOnly ForecastAvailableTime { get; set; } = new TimeOnly(6, 0);

    /// <summary>
    /// Scale factor applied to raw forecast to produce real-world estimate.
    /// Default 0.7 = 70% of forecast value.
    /// </summary>
    public double ScaleFactor { get; set; } = 0.7;

    /// <summary>
    /// Time of solar noon (local). Used for distributing forecast across slots.
    /// </summary>
    public TimeOnly SolarNoon { get; set; } = new TimeOnly(13, 0);

    /// <summary>
    /// Approximate number of productive solar hours per day (for bell-curve distribution).
    /// </summary>
    public double ProductionHours { get; set; } = 8.0;
}

public enum SolarForecastMode
{
    /// <summary>Read a single forecast value from a HomeAssistant entity.</summary>
    HomeAssistantEntity
}
