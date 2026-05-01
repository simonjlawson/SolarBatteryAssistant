using SolarBatteryAssistant.Core.Models;

namespace SolarBatteryAssistant.Simulator.Rates;

/// <summary>
/// A named snapshot of half-hourly energy prices that can be saved to and
/// loaded from disk.
/// </summary>
public class RateSet
{
    /// <summary>User-supplied name for this rate set (also used as the file name).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The calendar date the prices originally apply to.</summary>
    public DateOnly Date { get; set; }

    /// <summary>The 48 half-hourly price slots.</summary>
    public List<EnergyPrice> Prices { get; set; } = [];
}
