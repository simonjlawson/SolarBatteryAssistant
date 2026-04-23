namespace SolarBatteryAssistant.Core.Configuration;

/// <summary>
/// Configuration for the battery system.
/// </summary>
public class BatteryConfiguration
{
    /// <summary>HomeAssistant entity_id of the battery state-of-charge sensor (0-100%).</summary>
    public string SocEntityId { get; set; } = "sensor.battery_state_of_charge";

    /// <summary>Total usable capacity of the battery in Wh.</summary>
    public double CapacityWh { get; set; } = 10000;

    /// <summary>Maximum import power from grid into battery in Watts.</summary>
    public double MaxImportWatts { get; set; } = 3000;

    /// <summary>Maximum export power from battery to grid in Watts.</summary>
    public double MaxExportWatts { get; set; } = 3000;

    /// <summary>Minimum battery charge to maintain (%) to protect battery health.</summary>
    public double MinChargePercent { get; set; } = 10;

    /// <summary>Maximum battery charge target (%) — typically 100 unless schedule limiting.</summary>
    public double MaxChargePercent { get; set; } = 100;

    /// <summary>Round-trip efficiency of the battery (0-1). Default 0.9 = 90%.</summary>
    public double RoundTripEfficiency { get; set; } = 0.90;

    /// <summary>
    /// Estimated average house load in Watts (flat constant, used as fallback when
    /// <see cref="EstimatedDailyLoadWh"/> is zero).
    /// </summary>
    public double EstimatedHouseLoadWatts { get; set; } = 500;

    /// <summary>
    /// Total estimated daily house energy consumption in Wh.
    /// When greater than zero the planner distributes this across slots using a
    /// time-of-day profile rather than using the flat <see cref="EstimatedHouseLoadWatts"/>.
    /// Default 8000 Wh (8 kWh).
    /// </summary>
    public double EstimatedDailyLoadWh { get; set; } = 8000;

    /// <summary>
    /// Local hour (0–23) at which significant house load begins.
    /// The majority of <see cref="EstimatedDailyLoadWh"/> is concentrated between this hour
    /// and <see cref="LoadActiveEndHour"/>. Default 9 (09:00).
    /// </summary>
    public int LoadActiveStartHour { get; set; } = 9;

    /// <summary>
    /// Local hour (0–23) at which significant house load ends.
    /// Default 21 (21:00).
    /// </summary>
    public int LoadActiveEndHour { get; set; } = 21;

    /// <summary>
    /// Proportion of <see cref="EstimatedDailyLoadWh"/> that falls within the active window.
    /// The remainder is spread evenly across off-peak hours. Default 0.80 (80 %).
    /// </summary>
    public double LoadActiveProportion { get; set; } = 0.80;

    /// <summary>
    /// Optional HomeAssistant entity_id for real-time house load power in Watts.
    /// When set, this is used instead of <see cref="EstimatedHouseLoadWatts"/>.
    /// </summary>
    public string? HouseLoadEntityId { get; set; }
}
