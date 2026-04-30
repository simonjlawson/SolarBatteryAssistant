namespace SolarBatteryAssistant.Core.Models;

/// <summary>
/// Represents a 30-minute energy price slot.
/// </summary>
public class EnergyPrice
{
    /// <summary>Start of the 30-minute slot (UTC).</summary>
    public DateTimeOffset SlotStart { get; set; }

    /// <summary>End of the 30-minute slot (UTC).</summary>
    public DateTimeOffset SlotEnd { get; set; }

    /// <summary>Import (buy) price in pence per kWh.</summary>
    public decimal ImportPencePerKwh { get; set; }

    /// <summary>Export (sell) price in pence per kWh. Null if no export tariff available.</summary>
    public decimal? ExportPencePerKwh { get; set; }
}
