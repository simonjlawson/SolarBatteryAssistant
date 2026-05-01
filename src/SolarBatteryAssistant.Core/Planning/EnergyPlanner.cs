using SolarBatteryAssistant.Core.Configuration;
using SolarBatteryAssistant.Core.Interfaces;
using SolarBatteryAssistant.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SolarBatteryAssistant.Core.Planning;

/// <summary>
/// Greedy planning engine that assigns the cheapest battery action to each 30-minute
/// slot for the day, subject to battery capacity and power constraints.
///
/// Strategy (evaluated in priority order for each slot):
///   1. <b>ExportToGrid</b> — slot is among the most expensive export windows AND battery
///      has sufficient charge above minimum.
///   2. <b>ImportFromGrid</b> — slot is among the cheapest import windows OR the import
///      price is at or below <see cref="PlanningConfiguration.VeryCheapImportThresholdPence"/>
///      (catches negative and near-zero prices) AND battery is not already full AND no later
///      qualifying slot offers a strictly cheaper price (charging is deferred to the lowest
///      cost opportunity, including negative-price slots where income is maximised).
///   3. <b>BypassBatteryOnlyUseGrid</b> — battery is full and solar generation exceeds house
///      load; bypass to avoid unnecessary charge cycling.
///   4. <b>NormalBatteryMinimiseGrid</b> — default.
///
/// House load is distributed across the day using a configurable daily total with the
/// majority of consumption concentrated in a configurable active window (default 09:00–21:00).
/// An optional extra-load CSV (<see cref="PlanningConfiguration.ExtraLoadCsvPath"/>) is
/// added on top of the base profile for each hour.
///
/// Cost calculations include income from exporting surplus solar to the grid at either the
/// dynamic per-slot export price or a configured static export rate.
/// </summary>
public class EnergyPlanner : IEnergyPlanner
{
    private readonly PlanningConfiguration _planConfig;
    private readonly BatteryConfiguration _batteryConfig;
    private readonly SolarConfiguration _solarConfig;
    private readonly ILogger<EnergyPlanner> _logger;

    /// <summary>Cached extra-load profile (24 hourly Watt values), or null if not configured.</summary>
    private double[]? _extraLoadProfile;

    public EnergyPlanner(
        IOptions<DaemonConfiguration> config,
        ILogger<EnergyPlanner> logger)
    {
        _planConfig = config.Value.Planning;
        _batteryConfig = config.Value.Battery;
        _solarConfig = config.Value.Solar;
        _logger = logger;

        _extraLoadProfile = LoadExtraLoadProfile();
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

        var slots = BuildSlots(planDate, prices, solarProfile, tz);
        if (!slots.Any())
        {
            _logger.LogWarning("No price data available for {Date}. Plan will be empty.", planDate);
            plan.Slots = slots;
            return Task.FromResult(plan);
        }

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

        foreach (var slot in currentPlan.Slots.Where(s => s.SlotEnd <= now))
            slot.IsCompleted = true;

        var pendingSlots = currentPlan.Slots.Where(s => !s.IsCompleted).ToList();
        if (pendingSlots.Count > 0)
            AssignActions(pendingSlots, currentBatteryState.ChargePercent);

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

            // Resolve the effective export price: dynamic from tariff or static fallback
            decimal? effectiveExportPrice = price.ExportPencePerKwh ?? _planConfig.StaticExportPencePerKwh;

            // Create a price object with the effective export price applied so all
            // downstream calculations use the same price reference
            var resolvedPrice = effectiveExportPrice == price.ExportPencePerKwh
                ? price
                : new EnergyPrice
                {
                    SlotStart = price.SlotStart,
                    SlotEnd = price.SlotEnd,
                    ImportPencePerKwh = price.ImportPencePerKwh,
                    ExportPencePerKwh = effectiveExportPrice
                };

            slots.Add(new PlanSlot
            {
                SlotStart = price.SlotStart,
                SlotEnd = price.SlotEnd,
                Price = resolvedPrice,
                SolarWatts = solarWatts,
                EstimatedLoadWatts = SlotLoadHelper.ComputeSlotLoadWatts(
                    timeKey, _batteryConfig, _extraLoadProfile),
                Action = BatteryAction.NormalBatteryMinimiseGrid
            });
        }

        return slots;
    }

    private void AssignActions(List<PlanSlot> slots, double initialChargePercent)
    {
        if (!slots.Any()) return;

        // Identify cheap import windows (bottom 25% of prices by rank)
        var sortedByImport = slots.OrderBy(s => s.Price.ImportPencePerKwh).ToList();
        int cheapCount = Math.Max(1, sortedByImport.Count / 4);
        var cheapSlots = sortedByImport.Take(cheapCount).Select(s => s.SlotStart).ToHashSet();

        // Identify expensive export windows (top 25% of slots that have an export price)
        var exportableSlots = slots
            .Where(s => s.Price.ExportPencePerKwh.HasValue)
            .OrderByDescending(s => s.Price.ExportPencePerKwh!.Value)
            .ToList();
        int expensiveCount = Math.Max(1, exportableSlots.Count / 4);
        var expensiveExportSlots = exportableSlots.Take(expensiveCount).Select(s => s.SlotStart).ToHashSet();

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

            // A slot qualifies as "very cheap" if its price is at or below the configured
            // threshold — this covers negative prices and near-zero tariffs regardless of
            // how the day's prices rank relative to each other.
            bool isVeryCheap = slot.Price.ImportPencePerKwh <= _planConfig.VeryCheapImportThresholdPence;

            bool canCharge = _planConfig.AllowGridCharging
                && batteryPercent < maxPercent
                && (cheapSlots.Contains(slot.SlotStart) || isVeryCheap);

            // Defer charging if a later qualifying slot offers a strictly cheaper import price.
            // This ensures the battery is charged at the lowest possible cost (or maximum income
            // when prices are negative), rather than filling up too early at a higher price.
            if (canCharge)
            {
                bool hasLaterCheaperSlot = slots
                    .Any(s => s.SlotStart > slot.SlotStart
                        && (cheapSlots.Contains(s.SlotStart)
                            || s.Price.ImportPencePerKwh <= _planConfig.VeryCheapImportThresholdPence)
                        && s.Price.ImportPencePerKwh < slot.Price.ImportPencePerKwh);

                if (hasLaterCheaperSlot)
                    canCharge = false;
            }

            bool canExport = _planConfig.AllowExport
                && slot.Price.ExportPencePerKwh.HasValue
                && batteryPercent > minPercent
                && expensiveExportSlots.Contains(slot.SlotStart)
                // Only export if we earn more than we'd spend re-importing later
                && slot.Price.ExportPencePerKwh > slot.Price.ImportPencePerKwh;

            if (canExport)
            {
                action = BatteryAction.ExportToGrid;
                double exportWh = Math.Min(
                    _batteryConfig.MaxExportWatts * slotHours,
                    (batteryPercent - minPercent) / 100.0 * capacityWh);
                batteryDeltaWh = -exportWh;
            }
            else if (canCharge)
            {
                action = BatteryAction.ImportFromGrid;
                double importWh = Math.Min(
                    _batteryConfig.MaxImportWatts * slotHours,
                    (maxPercent - batteryPercent) / 100.0 * capacityWh);
                batteryDeltaWh = importWh * efficiency;
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
                // Solar charges battery or supplies load; battery makes up any shortfall.
                batteryDeltaWh = netSolarWatts * slotHours * (netSolarWatts > 0 ? efficiency : 1.0 / efficiency);
                batteryDeltaWh = Math.Max(
                    -capacityWh * (batteryPercent - minPercent) / 100.0,
                    Math.Min(capacityWh * (maxPercent - batteryPercent) / 100.0, batteryDeltaWh));
            }

            double newBatteryWh = Math.Clamp(batteryEnergyWh + batteryDeltaWh, 0, capacityWh);
            double newBatteryPercent = newBatteryWh / capacityWh * 100.0;

            slot.Action = action;
            slot.BatteryChargePercentStart = batteryPercent;
            slot.BatteryChargePercentEnd = newBatteryPercent;
            slot.EstimatedCostPence = SlotCostCalculator.Calculate(slot, action, batteryDeltaWh, _batteryConfig);

            batteryPercent = newBatteryPercent;
        }
    }

    private double[]? LoadExtraLoadProfile()
    {
        if (string.IsNullOrWhiteSpace(_planConfig.ExtraLoadCsvPath))
            return null;

        CsvProfileLoader.CreateExampleExtraLoadCsv(_planConfig.ExtraLoadCsvPath);

        try
        {
            return CsvProfileLoader.LoadExtraLoadHourlyWatts(_planConfig.ExtraLoadCsvPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not load extra load profile from '{Path}'. Extra load will be zero.",
                _planConfig.ExtraLoadCsvPath);
            return null;
        }
    }
}
