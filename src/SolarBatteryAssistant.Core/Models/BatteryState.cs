namespace SolarBatteryAssistant.Core.Models;

/// <summary>
/// Current state of the battery system.
/// </summary>
public class BatteryState
{
    /// <summary>Battery state of charge as a percentage (0-100).</summary>
    public double ChargePercent { get; set; }

    /// <summary>Timestamp of the reading.</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Derived usable energy in Wh based on capacity and charge percent.
    /// </summary>
    public double UsableEnergyWh(double capacityWh, double minChargePercent = 10.0)
    {
        double usablePercent = Math.Max(0, ChargePercent - minChargePercent);
        return capacityWh * usablePercent / 100.0;
    }
}
