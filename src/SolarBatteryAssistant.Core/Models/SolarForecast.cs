namespace SolarBatteryAssistant.Core.Models;

/// <summary>
/// Solar generation forecast for a day.
/// </summary>
public class SolarForecast
{
    /// <summary>Date this forecast applies to.</summary>
    public DateOnly ForecastDate { get; set; }

    /// <summary>Total predicted generation in Watts (raw, before scale factor).</summary>
    public double RawForecastWatts { get; set; }

    /// <summary>Scale factor applied (e.g. 0.7 = 70% of raw forecast).</summary>
    public double ScaleFactor { get; set; } = 0.7;

    /// <summary>Effective forecast after applying scale factor in Watts.</summary>
    public double EffectiveWatts => RawForecastWatts * ScaleFactor;

    /// <summary>Timestamp when the forecast was received.</summary>
    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>
    /// Distributes the total Watts forecast across 30-minute slots using a
    /// bell-curve approximation centered around solar noon (13:00 local time).
    /// Returns a dictionary keyed by slot start time with average Watts for that slot.
    /// </summary>
    public Dictionary<TimeOnly, double> GetHalfHourlyProfile(TimeOnly solarNoon = default, double productionHours = 8.0)
    {
        if (solarNoon == default)
            solarNoon = new TimeOnly(13, 0);

        var profile = new Dictionary<TimeOnly, double>();
        double noonMinutes = solarNoon.Hour * 60 + solarNoon.Minute;
        double halfWindowMinutes = (productionHours / 2.0) * 60;
        double sigma = halfWindowMinutes / 3.0; // 3-sigma covers the window

        // Generate slots from 00:00 to 23:30
        double totalWeight = 0;
        var weights = new Dictionary<TimeOnly, double>();

        for (int h = 0; h < 24; h++)
        {
            for (int m = 0; m < 60; m += 30)
            {
                var slot = new TimeOnly(h, m);
                double slotMinutes = h * 60 + m;
                double diff = slotMinutes - noonMinutes;
                double weight = Math.Exp(-(diff * diff) / (2 * sigma * sigma));
                weights[slot] = weight;
                totalWeight += weight;
            }
        }

        double totalEnergyWh = EffectiveWatts; // raw value is total Wh for day
        foreach (var kvp in weights)
        {
            // Each slot is 0.5h, convert to average Watts: Wh_for_slot / 0.5h
            double slotEnergyWh = totalEnergyWh * (kvp.Value / totalWeight);
            profile[kvp.Key] = slotEnergyWh / 0.5; // average Watts in slot
        }

        return profile;
    }
}
