using SolarBatteryAssistant.Core.Interfaces;
using SolarBatteryAssistant.Core.Models;

namespace SolarBatteryAssistant.Simulator.Demo;

/// <summary>
/// Generates synthetic Octopus Agile-style half-hourly prices for any requested date.
/// Prices follow a realistic pattern:
///   - Overnight (00:00–06:30): cheap (2–8 p/kWh)
///   - Morning peak (07:00–09:30): expensive (20–35 p/kWh)
///   - Daytime (10:00–16:00): moderate (10–18 p/kWh)
///   - Evening peak (17:00–19:30): expensive (25–40 p/kWh)
///   - Late evening (20:00–23:30): moderate falling back to cheap
/// Export prices are ~80 % of import prices.
/// A small pseudo-random spread is added seeded from the date so the same date
/// always yields the same prices.
/// </summary>
public class DemoEnergyPriceProvider : IEnergyPriceProvider
{
    // Base import prices by half-hour slot index (0 = 00:00, 1 = 00:30, ..., 47 = 23:30)
    private static readonly double[] BaseImportPrices =
    [
        5.0,  4.5,  4.0,  3.8,  3.5,  3.5,  3.8,  4.0,   // 00:00–03:30
        5.0,  6.5,  8.0,  9.0, 12.0, 16.0, 24.0, 32.0,   // 04:00–07:30
       35.0, 30.0, 26.0, 22.0, 18.0, 16.0, 14.0, 12.0,   // 08:00–11:30
       11.0, 10.5, 11.0, 12.0, 13.0, 14.0, 15.0, 17.0,   // 12:00–15:30
       19.0, 22.0, 26.0, 32.0, 38.0, 35.0, 30.0, 25.0,   // 16:00–19:30
       20.0, 16.0, 13.0, 10.0,  8.0,  7.0,  6.0,  5.5    // 20:00–23:30
    ];

    public Task<IReadOnlyList<EnergyPrice>> GetPricesForDateAsync(
        DateOnly date, CancellationToken cancellationToken = default)
    {
        var rng = new Random(date.DayNumber); // deterministic per date
        var prices = new List<EnergyPrice>(48);

        // UTC midnight for the requested date (assume UK = UTC for simplicity in demo)
        var dayStart = new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);

        for (int i = 0; i < 48; i++)
        {
            double jitter = (rng.NextDouble() - 0.5) * 3.0; // ±1.5 p
            double importP = Math.Max(0.5, BaseImportPrices[i] + jitter);
            double exportP = importP * 0.80;

            prices.Add(new EnergyPrice
            {
                SlotStart = dayStart.AddMinutes(i * 30),
                SlotEnd = dayStart.AddMinutes((i + 1) * 30),
                ImportPencePerKwh = (decimal)Math.Round(importP, 2),
                ExportPencePerKwh = (decimal)Math.Round(exportP, 2)
            });
        }

        return Task.FromResult<IReadOnlyList<EnergyPrice>>(prices);
    }

    public async Task<EnergyPrice?> GetCurrentPriceAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var prices = await GetPricesForDateAsync(today, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        return prices.FirstOrDefault(p => p.SlotStart <= now && p.SlotEnd > now);
    }
}
