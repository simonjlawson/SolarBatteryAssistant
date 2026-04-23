namespace SolarBatteryAssistant.Core.Configuration;

/// <summary>
/// Planning engine configuration.
/// </summary>
public class PlanningConfiguration
{
    /// <summary>
    /// How often to re-evaluate the plan (minutes). Default 30 = every half-hour slot.
    /// </summary>
    public int ReEvaluationIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// Threshold change in battery % that triggers an immediate re-evaluation.
    /// E.g. 10 = re-evaluate if battery is 10% different from predicted.
    /// </summary>
    public double BatteryDeviationThresholdPercent { get; set; } = 10.0;

    /// <summary>
    /// Minimum cost saving (pence) to justify changing the current action.
    /// Prevents excessive scene switching.
    /// </summary>
    public decimal MinSavingToChangePence { get; set; } = 5m;

    /// <summary>
    /// Target battery state of charge at end of day (%).
    /// Planner will aim to reach this level by midnight.
    /// </summary>
    public double EodTargetChargePercent { get; set; } = 20;

    /// <summary>
    /// Whether to allow exporting to grid when export prices are available.
    /// </summary>
    public bool AllowExport { get; set; } = true;

    /// <summary>
    /// Whether to allow importing from grid to charge battery.
    /// </summary>
    public bool AllowGridCharging { get; set; } = true;

    /// <summary>
    /// Time zone identifier for local time calculations (e.g. "Europe/London").
    /// </summary>
    public string TimeZoneId { get; set; } = "Europe/London";
}
