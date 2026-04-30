namespace SolarBatteryAssistant.Core.Models;

/// <summary>
/// Represents a single 30-minute slot within an energy plan.
/// </summary>
public class PlanSlot
{
    /// <summary>Start of the 30-minute slot (UTC).</summary>
    public DateTimeOffset SlotStart { get; set; }

    /// <summary>End of the 30-minute slot (UTC).</summary>
    public DateTimeOffset SlotEnd { get; set; }

    /// <summary>Planned battery action for this slot.</summary>
    public BatteryAction Action { get; set; }

    /// <summary>Energy price data for this slot.</summary>
    public EnergyPrice Price { get; set; } = null!;

    /// <summary>Predicted solar generation in Watts for this slot.</summary>
    public double SolarWatts { get; set; }

    /// <summary>Estimated house load in Watts for this slot.</summary>
    public double EstimatedLoadWatts { get; set; }

    /// <summary>Battery charge % at the start of this slot.</summary>
    public double BatteryChargePercentStart { get; set; }

    /// <summary>Battery charge % at the end of this slot (estimated).</summary>
    public double BatteryChargePercentEnd { get; set; }

    /// <summary>Estimated cost/income for this slot in pence. Negative = income.</summary>
    public decimal EstimatedCostPence { get; set; }

    /// <summary>
    /// Actual action taken (populated when reviewing historical data).
    /// </summary>
    public BatteryAction? ActualAction { get; set; }

    /// <summary>Actual cost/income for this slot (populated from metering).</summary>
    public decimal? ActualCostPence { get; set; }

    /// <summary>Indicates this slot has already been executed.</summary>
    public bool IsCompleted { get; set; }
}
