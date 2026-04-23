using SolarBatteryAssistant.Core.Configuration;
using SolarBatteryAssistant.Core.Interfaces;
using SolarBatteryAssistant.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SolarBatteryAssistant.Core.Planning;

/// <summary>
/// Greedy/dynamic planning engine that assigns the cheapest battery action to each
/// 30-minute slot for the day, subject to battery capacity and power constraints.
///
/// Strategy:
///   1. Sort slots by price to identify cheapest import and most expensive export opportunities.
///   2. For each slot in time order:
///      - If import price is in the cheapest N slots AND battery not full AND AllowGridCharging:
///        ImportFromGrid
///      - If export price is in the most expensive N slots AND battery has charge AND AllowExport:
///        ExportToGrid
///      - If solar generation > house load (excess solar) AND battery full:
///        BypassBatteryOnlyUseGrid  (avoid wasting charge cycles)
///      - Otherwise:
///        NormalBatteryMinimiseGrid
///   3. Battery SoC is simulated forward to enforce capacity constraints.
/// </summary>
public class EnergyPlanner : IEnergyPlanner
{
    private readonly PlanningConfiguration _planConfig;
    private readonly BatteryConfiguration _batteryConfig;
    private readonly SolarConfiguration _solarConfig;
    private readonly ILogger<EnergyPlanner> _logger;

    public EnergyPlanner(
        IOptions<DaemonConfiguration> config,
        ILogger<EnergyPlanner> logger)
    {
        _planConfig = config.Value.Planning;
        _batteryConfig = config.Value.Battery;
        _solarConfig = config.Value.Solar;
        _logger = logger;
    }

    public Task<EnergyPlan> GeneratePlanAsync(
        DateOnly planDate,
        IReadOnlyList<EnergyPrice> prices,
        SolarForecast solarForecast,
        BatteryState currentBatteryState,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Generating plan for {Date}. Battery: {Soc}%, Solar forecast: {Solar}Wh",
            planDate, currentBatteryState.ChargePercent, solarForecast.EffectiveWatts);

        var solarProfile = solarForecast.GetHalfHourlyProfile(
            _solarConfig.SolarNoon,
            _solarConfig.ProductionHours);

        // Resolve timezone for local time calculations
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(_planConfig.TimeZoneId); }
        catch { tz = TimeZoneInfo.Utc; }

        var plan = new EnergyPlan
        {
            PlanDate = planDate,
            GeneratedAt = DateTimeOffset.UtcNow,
            InitialBatteryState = currentBatteryState,
            SolarForecast = solarForecast
        };

        // Build slot list for the full day
        var slots = BuildSlots(planDate, prices, solarProfile, tz);
        if (!slots.Any())
        {
            _logger.LogWarning("No price data available for {Date}. Plan will be empty.", planDate);
            plan.Slots = slots;
            return Task.FromResult(plan);
        }

        // Assign actions using greedy optimiser
        AssignActions(slots, currentBatteryState.ChargePercent);

        plan.Slots = slots;

        _logger.LogInformation(
            "Plan generated: {SlotCount} slots, estimated cost {Cost:F2}p",
            plan.Slots.Count, plan.TotalEstimatedCostPence);

        return Task.FromResult(plan);
    }

    public Task<EnergyPlan> ReEvaluatePlanAsync(
        EnergyPlan currentPlan,
        BatteryState currentBatteryState,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Re-evaluating plan for {Date}. Battery: {Soc}%",
            currentPlan.PlanDate, currentBatteryState.ChargePercent);

        var now = DateTimeOffset.UtcNow;

        // Mark completed slots
        foreach (var slot in currentPlan.Slots.Where(s => s.SlotEnd <= now))
            slot.IsCompleted = true;

        // Re-optimise remaining slots with current battery state
        var pendingSlots = currentPlan.Slots.Where(s => !s.IsCompleted).ToList();
        if (pendingSlots.Count > 0)
        {
            AssignActions(pendingSlots, currentBatteryState.ChargePercent);
        }

        currentPlan.GeneratedAt = now;
        currentPlan.InitialBatteryState = currentBatteryState;

        return Task.FromResult(currentPlan);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private List<PlanSlot> BuildSlots(
        DateOnly planDate,
        IReadOnlyList<EnergyPrice> prices,
        Dictionary<TimeOnly, double> solarProfile,
        TimeZoneInfo tz)
    {
        var slots = new List<PlanSlot>();

        foreach (var price in prices.OrderBy(p => p.SlotStart))
        {
            var localStart = TimeZoneInfo.ConvertTime(price.SlotStart, tz);
            var timeKey = new TimeOnly(localStart.Hour, localStart.Minute);

            solarProfile.TryGetValue(timeKey, out double solarWatts);

            slots.Add(new PlanSlot
            {
                SlotStart = price.SlotStart,
                SlotEnd = price.SlotEnd,
                Price = price,
                SolarWatts = solarWatts,
                EstimatedLoadWatts = _batteryConfig.EstimatedHouseLoadWatts,
                Action = BatteryAction.NormalBatteryMinimiseGrid
            });
        }

        return slots;
    }

    private void AssignActions(List<PlanSlot> slots, double initialChargePercent)
    {
        if (!slots.Any()) return;

        // Identify cheap import windows (bottom 25% of prices)
        var sortedByImport = slots.OrderBy(s => s.Price.ImportPencePerKwh).ToList();
        int cheapCount = Math.Max(1, sortedByImport.Count / 4);
        var cheapSlots = sortedByImport.Take(cheapCount).Select(s => s.SlotStart).ToHashSet();

        // Identify expensive export windows (top 25% of prices where export exists)
        var exportSlots = slots
            .Where(s => s.Price.ExportPencePerKwh.HasValue)
            .OrderByDescending(s => s.Price.ExportPencePerKwh!.Value)
            .ToList();
        int expensiveCount = Math.Max(1, exportSlots.Count / 4);
        var expensiveExportSlots = exportSlots.Take(expensiveCount).Select(s => s.SlotStart).ToHashSet();

        double batteryPercent = initialChargePercent;
        double capacityWh = _batteryConfig.CapacityWh;
        double efficiency = _batteryConfig.RoundTripEfficiency;
        double minPercent = _batteryConfig.MinChargePercent;
        double maxPercent = _batteryConfig.MaxChargePercent;

        foreach (var slot in slots.OrderBy(s => s.SlotStart))
        {
            double batteryEnergyWh = capacityWh * batteryPercent / 100.0;
            double netSolarWatts = slot.SolarWatts - slot.EstimatedLoadWatts;
            double slotHours = 0.5;

            BatteryAction action;
            double batteryDeltaWh = 0;

            bool canCharge = _planConfig.AllowGridCharging
                && batteryPercent < maxPercent
                && cheapSlots.Contains(slot.SlotStart);

            bool canExport = _planConfig.AllowExport
                && slot.Price.ExportPencePerKwh.HasValue
                && batteryPercent > minPercent
                && expensiveExportSlots.Contains(slot.SlotStart);

            // Export takes priority over cheap import (can't do both)
            if (canExport && slot.Price.ExportPencePerKwh > slot.Price.ImportPencePerKwh)
            {
                action = BatteryAction.ExportToGrid;
                double exportWh = Math.Min(_batteryConfig.MaxExportWatts * slotHours,
                    (batteryPercent - minPercent) / 100.0 * capacityWh);
                batteryDeltaWh = -exportWh; // discharging
            }
            else if (canCharge)
            {
                action = BatteryAction.ImportFromGrid;
                double importWh = Math.Min(_batteryConfig.MaxImportWatts * slotHours,
                    (maxPercent - batteryPercent) / 100.0 * capacityWh);
                batteryDeltaWh = importWh * efficiency; // charging with losses
            }
            else if (netSolarWatts > 0 && batteryPercent >= maxPercent - 1)
            {
                // Battery full and excess solar — bypass to avoid unnecessary charge cycling
                action = BatteryAction.BypassBatteryOnlyUseGrid;
                batteryDeltaWh = 0;
            }
            else
            {
                action = BatteryAction.NormalBatteryMinimiseGrid;
                // In normal mode: solar charges battery or supplies load
                // Net effect on battery: if solar > load, charge; else discharge
                batteryDeltaWh = netSolarWatts * slotHours * (netSolarWatts > 0 ? efficiency : 1.0 / efficiency);
                batteryDeltaWh = Math.Max(-capacityWh * (batteryPercent - minPercent) / 100.0,
                    Math.Min(capacityWh * (maxPercent - batteryPercent) / 100.0, batteryDeltaWh));
            }

            double newBatteryWh = Math.Clamp(batteryEnergyWh + batteryDeltaWh, 0, capacityWh);
            double newBatteryPercent = newBatteryWh / capacityWh * 100.0;

            slot.Action = action;
            slot.BatteryChargePercentStart = batteryPercent;
            slot.BatteryChargePercentEnd = newBatteryPercent;
            slot.EstimatedCostPence = CalculateSlotCost(slot, action, batteryDeltaWh);

            batteryPercent = newBatteryPercent;
        }
    }

    private decimal CalculateSlotCost(PlanSlot slot, BatteryAction action, double batteryDeltaWh)
    {
        double slotHours = 0.5;
        decimal costPence = 0;

        switch (action)
        {
            case BatteryAction.ImportFromGrid:
                // Cost = grid import power * time * import price
                double importKwh = _batteryConfig.MaxImportWatts * slotHours / 1000.0;
                costPence = (decimal)(importKwh) * slot.Price.ImportPencePerKwh;
                // Also add house load cost minus solar contribution
                double netLoad = Math.Max(0, slot.EstimatedLoadWatts - slot.SolarWatts);
                costPence += (decimal)(netLoad * slotHours / 1000.0) * slot.Price.ImportPencePerKwh;
                break;

            case BatteryAction.ExportToGrid:
                // Income = export power * time * export price
                double exportKwh = Math.Abs(batteryDeltaWh) / 1000.0;
                decimal exportIncome = (decimal)exportKwh * (slot.Price.ExportPencePerKwh ?? 0);
                // House load still needs power (from solar or grid)
                double houseSolar = Math.Min(slot.SolarWatts, slot.EstimatedLoadWatts);
                double houseGrid = Math.Max(0, slot.EstimatedLoadWatts - houseSolar);
                decimal houseCost = (decimal)(houseGrid * slotHours / 1000.0) * slot.Price.ImportPencePerKwh;
                costPence = houseCost - exportIncome;
                break;

            case BatteryAction.BypassBatteryOnlyUseGrid:
                // Grid covers the house load (solar may reduce it)
                double bypassSolar = Math.Min(slot.SolarWatts, slot.EstimatedLoadWatts);
                double bypassGrid = Math.Max(0, slot.EstimatedLoadWatts - bypassSolar);
                costPence = (decimal)(bypassGrid * slotHours / 1000.0) * slot.Price.ImportPencePerKwh;
                break;

            case BatteryAction.NormalBatteryMinimiseGrid:
            default:
                // Grid covers any shortfall after solar and battery
                double normalNet = slot.EstimatedLoadWatts - slot.SolarWatts;
                if (normalNet > 0)
                {
                    // Need grid if battery also cannot cover it fully
                    double batteryCanSupplyWh = (slot.BatteryChargePercentStart - _batteryConfig.MinChargePercent) / 100.0 * _batteryConfig.CapacityWh;
                    double batterySupplyWatts = Math.Min(_batteryConfig.MaxExportWatts, batteryCanSupplyWh / slotHours);
                    double gridWatts = Math.Max(0, normalNet - batterySupplyWatts);
                    costPence = (decimal)(gridWatts * slotHours / 1000.0) * slot.Price.ImportPencePerKwh;
                }
                else
                {
                    // Excess solar — could be exported if SEG configured (ignored here)
                    costPence = 0;
                }
                break;
        }

        return costPence;
    }
}
