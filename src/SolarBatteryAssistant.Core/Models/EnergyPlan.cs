namespace SolarBatteryAssistant.Core.Models;

/// <summary>
/// A complete day's energy management plan.
/// </summary>
public class EnergyPlan
{
    /// <summary>The date this plan covers (local date).</summary>
    public DateOnly PlanDate { get; set; }

    /// <summary>When this plan was generated.</summary>
    public DateTimeOffset GeneratedAt { get; set; }

    /// <summary>Battery state at the time the plan was generated.</summary>
    public BatteryState InitialBatteryState { get; set; } = null!;

    /// <summary>Solar forecast used to generate this plan.</summary>
    public SolarForecast SolarForecast { get; set; } = null!;

    /// <summary>Ordered list of 30-minute slots for the plan.</summary>
    public List<PlanSlot> Slots { get; set; } = [];

    /// <summary>Total estimated cost in pence (negative = net income).</summary>
    public decimal TotalEstimatedCostPence => Slots.Sum(s => s.EstimatedCostPence);

    /// <summary>Total actual cost if plan has been fully executed.</summary>
    public decimal? TotalActualCostPence =>
        Slots.All(s => s.ActualCostPence.HasValue)
            ? Slots.Sum(s => s.ActualCostPence!.Value)
            : null;

    /// <summary>
    /// Returns the slot for the current half-hour period.
    /// </summary>
    public PlanSlot? GetCurrentSlot(DateTimeOffset now)
    {
        return Slots.FirstOrDefault(s => s.SlotStart <= now && s.SlotEnd > now);
    }

    /// <summary>
    /// Returns slots that are still pending execution.
    /// </summary>
    public IEnumerable<PlanSlot> PendingSlots(DateTimeOffset now)
    {
        return Slots.Where(s => s.SlotStart >= now);
    }
}
