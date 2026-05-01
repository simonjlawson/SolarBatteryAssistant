using SolarBatteryAssistant.Core.Configuration;

namespace SolarBatteryAssistant.Core.Planning;

/// <summary>
/// Shared helper for computing the estimated house load in Watts for a given half-hour slot.
/// Used by both <see cref="EnergyPlanner"/> and <see cref="BruteForceEnergyPlanner"/> so the
/// load calculation is consistent across planning modes.
/// </summary>
internal static class SlotLoadHelper
{
    /// <summary>
    /// Returns the estimated house load in Watts for a given local half-hour slot.
    ///
    /// When <see cref="BatteryConfiguration.EstimatedDailyLoadWh"/> is greater than zero the
    /// load is distributed using an active-window profile; otherwise the flat
    /// <see cref="BatteryConfiguration.EstimatedHouseLoadWatts"/> is used.
    ///
    /// An optional <paramref name="extraLoadProfile"/> (24 hourly Watt values, index = hour)
    /// is added on top of the base profile when provided.
    /// </summary>
    /// <param name="localSlotTime">Local start time of the 30-minute slot.</param>
    /// <param name="batteryConfig">Battery configuration providing load parameters.</param>
    /// <param name="extraLoadProfile">
    /// Optional 24-element array of extra Watt values (one per hour).
    /// When <c>null</c> no extra load is added.
    /// </param>
    public static double ComputeSlotLoadWatts(
        TimeOnly localSlotTime,
        BatteryConfiguration batteryConfig,
        double[]? extraLoadProfile)
    {
        double baseWatts = ComputeBaseSlotWatts(localSlotTime, batteryConfig);

        double extraWatts = extraLoadProfile is not null
            ? extraLoadProfile[localSlotTime.Hour]
            : 0;

        return baseWatts + extraWatts;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static double ComputeBaseSlotWatts(TimeOnly localSlotTime, BatteryConfiguration batteryConfig)
    {
        if (batteryConfig.EstimatedDailyLoadWh <= 0)
            return batteryConfig.EstimatedHouseLoadWatts + batteryConfig.ConstantLoadWatts;

        int startH = batteryConfig.LoadActiveStartHour;
        int endH = batteryConfig.LoadActiveEndHour;
        double dailyWh = batteryConfig.EstimatedDailyLoadWh;
        double activeProp = Math.Clamp(batteryConfig.LoadActiveProportion, 0.0, 1.0);

        // Clamp to valid range (end > start, both 0-23)
        if (startH < 0 || startH >= 24) startH = 9;
        if (endH <= startH || endH > 24) endH = startH + 12;

        int activeHours = endH - startH;
        int offPeakHours = 24 - activeHours;

        // Watts per slot: activeWh = dailyWh * activeProp, spread across activeHours hours.
        double activeWatts = activeHours > 0
            ? (dailyWh * activeProp) / activeHours
            : 0;
        double offPeakWatts = offPeakHours > 0
            ? (dailyWh * (1 - activeProp)) / offPeakHours
            : 0;

        int hour = localSlotTime.Hour;
        bool inActiveWindow = hour >= startH && hour < endH;
        return (inActiveWindow ? activeWatts : offPeakWatts) + batteryConfig.ConstantLoadWatts;
    }
}
