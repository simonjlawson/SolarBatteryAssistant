using SolarBatteryAssistant.Core.Interfaces;
using SolarBatteryAssistant.Core.Models;

namespace SolarBatteryAssistant.Simulator.Demo;

/// <summary>
/// Returns a configurable synthetic solar forecast.
/// The daily total scales with the season (more in summer, less in winter)
/// and a small random spread is added seeded from the date.
/// </summary>
public class DemoSolarForecastProvider : ISolarForecastProvider
{
    /// <summary>Peak summer daily generation in Wh (raw before scale factor).</summary>
    public double PeakDailyWh { get; set; } = 20_000;

    /// <summary>Scale factor applied to the raw forecast.</summary>
    public double ScaleFactor { get; set; } = 0.7;

    public Task<SolarForecast?> GetForecastAsync(
        DateOnly date, CancellationToken cancellationToken = default)
    {
        // Seasonal factor: 1.0 at summer solstice (Jun 21), ~0.2 at winter solstice (Dec 21)
        double dayOfYear = date.DayOfYear;
        double summerSolsticeDoy = 172; // ~June 21
        double seasonalFactor = 0.6 + 0.4 * Math.Cos(2 * Math.PI * (dayOfYear - summerSolsticeDoy) / 365.0);

        var rng = new Random(date.DayNumber + 1);
        double cloudFactor = 0.7 + rng.NextDouble() * 0.5; // 0.7–1.2
        double rawWh = PeakDailyWh * seasonalFactor * cloudFactor;

        return Task.FromResult<SolarForecast?>(new SolarForecast
        {
            ForecastDate = date,
            RawForecastWatts = Math.Round(rawWh, 0),
            ScaleFactor = ScaleFactor,
            ReceivedAt = DateTimeOffset.UtcNow
        });
    }
}
