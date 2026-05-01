using SolarBatteryAssistant.Core.Configuration;
using SolarBatteryAssistant.Core.Interfaces;
using SolarBatteryAssistant.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SolarBatteryAssistant.Core.Planning;

/// <summary>
/// Bruteforce planning engine that finds the globally optimal (lowest cost / maximum income)
/// battery action sequence for the day using dynamic programming (DP).
///
/// <para>
/// The planner builds the same set of 30-minute slots as <see cref="EnergyPlanner"/> but,
/// instead of applying a greedy heuristic, it exhaustively evaluates every valid combination
/// of <see cref="BatteryAction"/> values across all slots and returns the sequence with the
/// lowest total estimated cost.
/// </para>
///
/// <para><b>Algorithm:</b></para>
/// <list type="number">
///   <item>Build slots (prices + solar + load, incorporating the optional extra-load CSV).</item>
///   <item>Prune the candidate action set per slot to eliminate obviously unprofitable actions.</item>
///   <item>
///     Run forward DP over <c>slots × battery_states</c>.
///     Battery state is quantised to integer percent (0–100) to keep the state space small:
///     O(48 slots × 101 states × 4 actions) ≈ 19 000 operations — fast enough to run each cycle.
///   </item>
///   <item>Backtrack through the DP table to recover the optimal action sequence.</item>
///   <item>Apply the recovered actions to produce the final <see cref="EnergyPlan"/>.</item>
/// </list>
///
/// <para><b>Sell-and-recharge logic:</b></para>
/// Slots with an import unit rate below
/// <see cref="PlanningConfiguration.ExportBatterySellThresholdPence"/> are flagged as prime
/// "sell stored energy, then recharge cheaply" opportunities.  The DP naturally discovers this
/// pattern — it will export battery energy in those slots when the resulting income outweighs
/// the cost of recharging at the cheapest later slot.
///
/// <para><b>Configuration overrides available to this planner:</b></para>
/// <list type="bullet">
///   <item><see cref="PlanningConfiguration.BruteForceStartingBatteryPercent"/> — override initial SOC.</item>
///   <item><see cref="PlanningConfiguration.ForecastedSolarWh"/> — override total daily solar.</item>
///   <item><see cref="PlanningConfiguration.SolarHourlyProfileCsvPath"/> — 24-hour solar shape CSV.</item>
///   <item><see cref="PlanningConfiguration.ExtraLoadCsvPath"/> — 24-hour extra load CSV.</item>
/// </list>
/// </summary>
public class BruteForceEnergyPlanner : IEnergyPlanner
{
    private readonly PlanningConfiguration _planConfig;
    private readonly BatteryConfiguration _batteryConfig;
    private readonly SolarConfiguration _solarConfig;
    private readonly ILogger<BruteForceEnergyPlanner> _logger;

    /// <summary>Cached extra-load profile (24 hourly Watt values), or null if not configured.</summary>
    private readonly double[]? _extraLoadProfile;

    /// <summary>Cached solar hourly percentage weights (24 values), or null → use bell-curve.</summary>
    private readonly double[]? _solarHourlyWeights;

    public BruteForceEnergyPlanner(
        IOptions<DaemonConfiguration> config,
        ILogger<BruteForceEnergyPlanner> logger)
    {
        _planConfig = config.Value.Planning;
        _batteryConfig = config.Value.Battery;
        _solarConfig = config.Value.Solar;
        _logger = logger;

        _extraLoadProfile = LoadExtraLoadProfile();
        _solarHourlyWeights = LoadSolarProfile();
    }

    public Task<EnergyPlan> GeneratePlanAsync(
        DateOnly planDate,
        IReadOnlyList<EnergyPrice> prices,
        SolarForecast solarForecast,
        BatteryState currentBatteryState,
        CancellationToken cancellationToken = default)
    {
        // Apply config overrides
        double startingBatteryPercent = _planConfig.BruteForceStartingBatteryPercent
            ?? currentBatteryState.ChargePercent;

        SolarForecast effectiveForecast = solarForecast;
        if (_planConfig.ForecastedSolarWh.HasValue)
        {
            effectiveForecast = new SolarForecast
            {
                ForecastDate = solarForecast.ForecastDate,
                RawForecastWatts = _planConfig.ForecastedSolarWh.Value,
                ScaleFactor = 1.0, // already the effective value
                ReceivedAt = solarForecast.ReceivedAt
            };
        }

        _logger.LogInformation(
            "BruteForce planner generating plan for {Date}. " +
            "Battery: {Soc}% (override={Override}), Solar: {Solar}Wh",
            planDate,
            startingBatteryPercent,
            _planConfig.BruteForceStartingBatteryPercent.HasValue,
            effectiveForecast.EffectiveWatts);

        // Build solar half-hourly profile
        Dictionary<TimeOnly, double> solarProfile = _solarHourlyWeights is not null
            ? effectiveForecast.GetHalfHourlyProfile(_solarHourlyWeights)
            : effectiveForecast.GetHalfHourlyProfile(_solarConfig.SolarNoon, _solarConfig.ProductionHours);

        // Resolve timezone
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(_planConfig.TimeZoneId); }
        catch { tz = TimeZoneInfo.Utc; }

        var plan = new EnergyPlan
        {
            PlanDate = planDate,
            GeneratedAt = DateTimeOffset.UtcNow,
            InitialBatteryState = currentBatteryState,
            SolarForecast = effectiveForecast
        };

        var slots = BuildSlots(prices, solarProfile, tz);
        if (!slots.Any())
        {
            _logger.LogWarning("No price data available for {Date}. Plan will be empty.", planDate);
            plan.Slots = slots;
            return Task.FromResult(plan);
        }

        RunDynamicProgramming(slots, startingBatteryPercent);

        plan.Slots = slots;

        _logger.LogInformation(
            "BruteForce plan generated: {SlotCount} slots, estimated cost {Cost:F2}p",
            plan.Slots.Count, plan.TotalEstimatedCostPence);

        return Task.FromResult(plan);
    }

    public Task<EnergyPlan> ReEvaluatePlanAsync(
        EnergyPlan currentPlan,
        BatteryState currentBatteryState,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "BruteForce planner re-evaluating plan for {Date}. Battery: {Soc}%",
            currentPlan.PlanDate, currentBatteryState.ChargePercent);

        var now = DateTimeOffset.UtcNow;

        foreach (var slot in currentPlan.Slots.Where(s => s.SlotEnd <= now))
            slot.IsCompleted = true;

        var pendingSlots = currentPlan.Slots.Where(s => !s.IsCompleted).ToList();
        if (pendingSlots.Count > 0)
        {
            double startingPercent = _planConfig.BruteForceStartingBatteryPercent
                ?? currentBatteryState.ChargePercent;
            RunDynamicProgramming(pendingSlots, startingPercent);
        }

        currentPlan.GeneratedAt = now;
        currentPlan.InitialBatteryState = currentBatteryState;

        return Task.FromResult(currentPlan);
    }

    // -------------------------------------------------------------------------
    // Slot building
    // -------------------------------------------------------------------------

    private List<PlanSlot> BuildSlots(
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

            decimal? effectiveExportPrice = price.ExportPencePerKwh ?? _planConfig.StaticExportPencePerKwh;
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

    // -------------------------------------------------------------------------
    // Dynamic programming
    // -------------------------------------------------------------------------

    /// <summary>
    /// Runs the DP over <paramref name="slots"/> (in chronological order) and writes
    /// the optimal action, battery SOC, and cost back to each slot in-place.
    /// </summary>
    private void RunDynamicProgramming(List<PlanSlot> slots, double initialChargePercent)
    {
        if (!slots.Any()) return;

        var orderedSlots = slots.OrderBy(s => s.SlotStart).ToArray();
        int n = orderedSlots.Length;

        // Battery quantisation: 0–100 % in 1 % steps → 101 states.
        const int States = 101;

        double capacityWh = _batteryConfig.CapacityWh;
        double efficiency = _batteryConfig.RoundTripEfficiency;
        double minPct = _batteryConfig.MinChargePercent;
        double maxPct = _batteryConfig.MaxChargePercent;

        // Compute average import price for pruning
        decimal avgImportPrice = orderedSlots.Length > 0
            ? orderedSlots.Average(s => s.Price.ImportPencePerKwh)
            : 0;

        // DP table: dp[slot][batteryPct] = minimum total cost from slot 0 to this state.
        // Use decimal.MaxValue to represent unreachable states.
        var dp = new decimal[n + 1][];
        var from = new (int prevPct, BatteryAction action)[n][];

        for (int i = 0; i <= n; i++)
            dp[i] = Enumerable.Repeat(decimal.MaxValue, States).ToArray();
        for (int i = 0; i < n; i++)
            from[i] = new (int, BatteryAction)[States];

        // Seed: initial battery state (quantised to nearest integer %)
        int initPct = QuantisePct(initialChargePercent);
        dp[0][initPct] = 0m;

        // Forward pass
        for (int i = 0; i < n; i++)
        {
            var slot = orderedSlots[i];
            var candidateActions = GetCandidateActions(slot, avgImportPrice);

            for (int pct = 0; pct < States; pct++)
            {
                if (dp[i][pct] == decimal.MaxValue) continue;

                foreach (var action in candidateActions)
                {
                    // Skip actions that are invalid given the current battery state
                    if (action == BatteryAction.ExportToGrid && pct <= (int)minPct) continue;
                    if (action == BatteryAction.ImportFromGrid && pct >= (int)maxPct) continue;

                    double batteryDeltaWh = ComputeBatteryDelta(action, pct, slot, capacityWh, efficiency, minPct, maxPct);
                    int newPct = QuantisePct(
                        Math.Clamp((capacityWh * pct / 100.0 + batteryDeltaWh) / capacityWh * 100.0, 0, 100));

                    // Populate slot with temporary values so SlotCostCalculator can read them
                    slot.BatteryChargePercentStart = pct;
                    slot.BatteryChargePercentEnd = newPct;

                    decimal slotCost = SlotCostCalculator.Calculate(slot, action, batteryDeltaWh, _batteryConfig);
                    decimal totalCost = dp[i][pct] + slotCost;

                    if (totalCost < dp[i + 1][newPct])
                    {
                        dp[i + 1][newPct] = totalCost;
                        from[i][newPct] = (pct, action);
                    }
                }
            }
        }

        // Find the optimal final battery state
        int finalPct = 0;
        decimal bestCost = decimal.MaxValue;
        for (int pct = 0; pct < States; pct++)
        {
            if (dp[n][pct] < bestCost)
            {
                bestCost = dp[n][pct];
                finalPct = pct;
            }
        }

        // Backtrack: recover the optimal action sequence
        var optimalActions = new BatteryAction[n];
        var optimalPcts = new int[n + 1];
        optimalPcts[n] = finalPct;

        for (int i = n - 1; i >= 0; i--)
        {
            var (prevPct, action) = from[i][optimalPcts[i + 1]];
            optimalActions[i] = action;
            optimalPcts[i] = prevPct;
        }

        // Write results back to slots
        for (int i = 0; i < n; i++)
        {
            var slot = orderedSlots[i];
            BatteryAction action = optimalActions[i];
            int pct = optimalPcts[i];
            int newPct = optimalPcts[i + 1];

            slot.BatteryChargePercentStart = pct;
            slot.BatteryChargePercentEnd = newPct;
            slot.Action = action;

            double batteryDeltaWh = ComputeBatteryDelta(action, pct, slot, capacityWh, efficiency, minPct, maxPct);
            slot.EstimatedCostPence = SlotCostCalculator.Calculate(slot, action, batteryDeltaWh, _batteryConfig);
        }
    }

    /// <summary>
    /// Computes the battery energy delta (Wh) for the given action and battery state.
    /// Positive = energy added to battery; negative = energy removed.
    /// </summary>
    private double ComputeBatteryDelta(
        BatteryAction action,
        int batteryPct,
        PlanSlot slot,
        double capacityWh,
        double efficiency,
        double minPct,
        double maxPct)
    {
        const double slotHours = 0.5;

        switch (action)
        {
            case BatteryAction.ImportFromGrid:
            {
                double importWh = Math.Min(
                    _batteryConfig.MaxImportWatts * slotHours,
                    (maxPct - batteryPct) / 100.0 * capacityWh);
                return importWh * efficiency;
            }

            case BatteryAction.ExportToGrid:
            {
                double exportWh = Math.Min(
                    _batteryConfig.MaxExportWatts * slotHours,
                    (batteryPct - minPct) / 100.0 * capacityWh);
                return -exportWh;
            }

            case BatteryAction.BypassBatteryOnlyUseGrid:
                return 0;

            case BatteryAction.NormalBatteryMinimiseGrid:
            default:
            {
                double netSolarWatts = slot.SolarWatts - slot.EstimatedLoadWatts;
                double delta = netSolarWatts * slotHours * (netSolarWatts > 0 ? efficiency : 1.0 / efficiency);
                delta = Math.Max(
                    -capacityWh * (batteryPct - minPct) / 100.0,
                    Math.Min(capacityWh * (maxPct - batteryPct) / 100.0, delta));
                return delta;
            }
        }
    }

    /// <summary>
    /// Returns the pruned set of candidate <see cref="BatteryAction"/> values for a slot.
    ///
    /// Pruning rules (applied in addition to per-state battery checks in the DP loop):
    /// <list type="bullet">
    ///   <item>If <see cref="PlanningConfiguration.AllowExport"/> is false, drop <c>ExportToGrid</c>.</item>
    ///   <item>If <see cref="PlanningConfiguration.AllowGridCharging"/> is false, drop <c>ImportFromGrid</c>.</item>
    ///   <item>Drop <c>ExportToGrid</c> when export price ≤ import price (would lose money on re-import).</item>
    ///   <item>Drop <c>ImportFromGrid</c> when import price is above average (no benefit to expensive charging),
    ///         UNLESS the import price is below <see cref="PlanningConfiguration.ExportBatterySellThresholdPence"/>
    ///         (sell-and-recharge slots should always allow import).</item>
    /// </list>
    /// </summary>
    private List<BatteryAction> GetCandidateActions(PlanSlot slot, decimal avgImportPrice)
    {
        var actions = new List<BatteryAction>(4)
        {
            BatteryAction.NormalBatteryMinimiseGrid,
            BatteryAction.BypassBatteryOnlyUseGrid
        };

        bool isSellAndRechargeSlot = slot.Price.ImportPencePerKwh < _planConfig.ExportBatterySellThresholdPence;

        if (_planConfig.AllowGridCharging)
        {
            // Allow charging at any cheap or sell-and-recharge slot; skip expensively-priced slots
            if (isSellAndRechargeSlot || slot.Price.ImportPencePerKwh <= avgImportPrice)
                actions.Add(BatteryAction.ImportFromGrid);
        }

        if (_planConfig.AllowExport && slot.Price.ExportPencePerKwh.HasValue)
        {
            // Only export when we earn more than the import price (otherwise we'd lose money re-importing)
            if (slot.Price.ExportPencePerKwh.Value > slot.Price.ImportPencePerKwh || isSellAndRechargeSlot)
                actions.Add(BatteryAction.ExportToGrid);
        }

        return actions;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static int QuantisePct(double pct) => (int)Math.Round(Math.Clamp(pct, 0, 100));

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

    private double[]? LoadSolarProfile()
    {
        if (string.IsNullOrWhiteSpace(_planConfig.SolarHourlyProfileCsvPath))
            return null;

        try
        {
            return CsvProfileLoader.LoadSolarHourlyPercentages(_planConfig.SolarHourlyProfileCsvPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not load solar hourly profile from '{Path}'. Bell-curve distribution will be used.",
                _planConfig.SolarHourlyProfileCsvPath);
            return null;
        }
    }
}
