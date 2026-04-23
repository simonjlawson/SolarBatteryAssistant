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
    /// Import price threshold in pence/kWh.  Any slot with an import price at or below
    /// this value is treated as "very cheap" and triggers grid charging regardless of where
    /// the slot ranks among the day's prices.  This ensures negative or near-zero prices
    /// always trigger charging even on days where all prices are low.
    /// Default 2.0 p/kWh.
    /// </summary>
    public decimal VeryCheapImportThresholdPence { get; set; } = 2.0m;

    /// <summary>
    /// Fixed export rate in pence/kWh applied to all export calculations when set.
    /// When <c>null</c> the planner uses the dynamic per-slot export price from the tariff
    /// provider (e.g. Octopus Agile export).  Set this to your SEG or fixed export rate
    /// if you do not have a dynamic export tariff.
    /// </summary>
    public decimal? StaticExportPencePerKwh { get; set; }

    /// <summary>
    /// Time zone identifier for local time calculations (e.g. "Europe/London").
    /// </summary>
    public string TimeZoneId { get; set; } = "Europe/London";
}
