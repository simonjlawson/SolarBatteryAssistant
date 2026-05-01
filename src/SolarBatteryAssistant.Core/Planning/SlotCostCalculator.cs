using SolarBatteryAssistant.Core.Configuration;
using SolarBatteryAssistant.Core.Models;

namespace SolarBatteryAssistant.Core.Planning;

/// <summary>
/// Shared cost-calculation logic for a single 30-minute plan slot.
/// Used by both <see cref="EnergyPlanner"/> and <see cref="BruteForceEnergyPlanner"/>.
/// </summary>
internal static class SlotCostCalculator
{
    private const double SlotHours = 0.5;

    /// <summary>
    /// Calculates the estimated cost (positive = spend, negative = income) for a plan slot
    /// given the battery action taken and the net battery energy delta in Wh.
    /// </summary>
    /// <param name="slot">The plan slot containing price, solar and load data.</param>
    /// <param name="action">The battery action assigned to this slot.</param>
    /// <param name="batteryDeltaWh">
    /// Net energy added to (+) or removed from (−) the battery in Wh.
    /// For <see cref="BatteryAction.ImportFromGrid"/> this is positive (battery gained energy).
    /// For <see cref="BatteryAction.ExportToGrid"/> this is negative (battery lost energy).
    /// </param>
    /// <param name="batteryConfig">Battery configuration (capacity, efficiency, limits).</param>
    /// <returns>Estimated cost in pence. Negative values indicate net income.</returns>
    public static decimal Calculate(
        PlanSlot slot,
        BatteryAction action,
        double batteryDeltaWh,
        BatteryConfiguration batteryConfig)
    {
        return action switch
        {
            BatteryAction.ImportFromGrid => CalculateImport(slot, batteryDeltaWh, batteryConfig),
            BatteryAction.ExportToGrid => CalculateExport(slot, batteryDeltaWh),
            BatteryAction.BypassBatteryOnlyUseGrid => CalculateBypass(slot),
            _ => CalculateNormal(slot, batteryConfig)
        };
    }

    // -------------------------------------------------------------------------
    // Per-action helpers
    // -------------------------------------------------------------------------

    private static decimal CalculateImport(PlanSlot slot, double batteryDeltaWh, BatteryConfiguration batteryConfig)
    {
        // Pay to charge battery from grid (account for round-trip efficiency loss)
        double importKwh = Math.Abs(batteryDeltaWh) / (1000.0 * batteryConfig.RoundTripEfficiency);
        decimal costPence = (decimal)importKwh * slot.Price.ImportPencePerKwh;

        // Also pay for any house load not covered by solar
        double netLoad = Math.Max(0, slot.EstimatedLoadWatts - slot.SolarWatts);
        costPence += (decimal)(netLoad * SlotHours / 1000.0) * slot.Price.ImportPencePerKwh;

        // Income from any surplus solar exported while the battery is charging
        double solarSurplus = Math.Max(0, slot.SolarWatts - slot.EstimatedLoadWatts);
        if (solarSurplus > 0 && slot.Price.ExportPencePerKwh.HasValue)
            costPence -= (decimal)(solarSurplus * SlotHours / 1000.0) * slot.Price.ExportPencePerKwh.Value;

        return costPence;
    }

    private static decimal CalculateExport(PlanSlot slot, double batteryDeltaWh)
    {
        // Income from exporting battery energy to grid
        double exportKwh = Math.Abs(batteryDeltaWh) / 1000.0;
        decimal exportIncome = (decimal)exportKwh * (slot.Price.ExportPencePerKwh ?? 0);

        // Additional income from any surplus solar exported on top
        double solarSurplus = Math.Max(0, slot.SolarWatts - slot.EstimatedLoadWatts);
        if (solarSurplus > 0 && slot.Price.ExportPencePerKwh.HasValue)
            exportIncome += (decimal)(solarSurplus * SlotHours / 1000.0) * slot.Price.ExportPencePerKwh.Value;

        // Cost to supply house load from grid (solar partially offsets this)
        double houseSolar = Math.Min(slot.SolarWatts, slot.EstimatedLoadWatts);
        double houseGrid = Math.Max(0, slot.EstimatedLoadWatts - houseSolar);
        decimal houseCost = (decimal)(houseGrid * SlotHours / 1000.0) * slot.Price.ImportPencePerKwh;

        return houseCost - exportIncome;
    }

    private static decimal CalculateBypass(PlanSlot slot)
    {
        // Grid covers any load not met by solar
        double loadFromGrid = Math.Max(0, slot.EstimatedLoadWatts - slot.SolarWatts);
        decimal costPence = (decimal)(loadFromGrid * SlotHours / 1000.0) * slot.Price.ImportPencePerKwh;

        // Income from exporting any surplus solar
        double solarSurplus = Math.Max(0, slot.SolarWatts - slot.EstimatedLoadWatts);
        if (solarSurplus > 0 && slot.Price.ExportPencePerKwh.HasValue)
            costPence -= (decimal)(solarSurplus * SlotHours / 1000.0) * slot.Price.ExportPencePerKwh.Value;

        return costPence;
    }

    private static decimal CalculateNormal(PlanSlot slot, BatteryConfiguration batteryConfig)
    {
        double normalNet = slot.EstimatedLoadWatts - slot.SolarWatts;
        if (normalNet > 0)
        {
            // Battery + solar together cover load; grid covers any remaining shortfall
            double batteryCanSupplyWh =
                (slot.BatteryChargePercentStart - batteryConfig.MinChargePercent) / 100.0
                * batteryConfig.CapacityWh;
            double batterySupplyWatts =
                Math.Min(batteryConfig.MaxExportWatts, batteryCanSupplyWh / SlotHours);
            double gridWatts = Math.Max(0, normalNet - batterySupplyWatts);
            return (decimal)(gridWatts * SlotHours / 1000.0) * slot.Price.ImportPencePerKwh;
        }
        else
        {
            // Excess solar — export surplus if an export price is available
            double solarSurplus = -normalNet;
            if (slot.Price.ExportPencePerKwh.HasValue)
                return -(decimal)(solarSurplus * SlotHours / 1000.0) * slot.Price.ExportPencePerKwh.Value;
            return 0;
        }
    }
}
