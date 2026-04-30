using SolarBatteryAssistant.Core.Interfaces;
using SolarBatteryAssistant.Core.Models;

namespace SolarBatteryAssistant.Simulator.Rates;

/// <summary>
/// An <see cref="IEnergyPriceProvider"/> that returns a fixed list of prices
/// supplied at construction time.
///
/// When a date other than the original date is requested the slot times are
/// shifted to the requested date so the planner always receives a full set of
/// 48 half-hourly slots regardless of which day is being planned.
/// </summary>
public class StaticEnergyPriceProvider : IEnergyPriceProvider
{
    private readonly IReadOnlyList<EnergyPrice> _prices;
    private readonly DateOnly _originalDate;

    public StaticEnergyPriceProvider(IReadOnlyList<EnergyPrice> prices)
    {
        _prices = prices;
        _originalDate = prices.Count > 0
            ? DateOnly.FromDateTime(prices[0].SlotStart.Date)
            : DateOnly.FromDateTime(DateTime.Today);
    }

    public Task<IReadOnlyList<EnergyPrice>> GetPricesForDateAsync(
        DateOnly date, CancellationToken cancellationToken = default)
    {
        if (date == _originalDate)
            return Task.FromResult(_prices);

        // Shift the slot times to match the requested date
        int dayDelta = date.DayNumber - _originalDate.DayNumber;
        IReadOnlyList<EnergyPrice> shifted = _prices
            .Select(p => new EnergyPrice
            {
                SlotStart = p.SlotStart.AddDays(dayDelta),
                SlotEnd = p.SlotEnd.AddDays(dayDelta),
                ImportPencePerKwh = p.ImportPencePerKwh,
                ExportPencePerKwh = p.ExportPencePerKwh
            })
            .ToList();

        return Task.FromResult(shifted);
    }

    public Task<EnergyPrice?> GetCurrentPriceAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTimeOffset.UtcNow;

        // Shift slot times to today if the stored prices are from a different date
        int dayDelta = today.DayNumber - _originalDate.DayNumber;
        if (dayDelta == 0)
            return Task.FromResult(_prices.FirstOrDefault(p => p.SlotStart <= now && p.SlotEnd > now));

        return Task.FromResult(
            _prices
                .Select(p => new EnergyPrice
                {
                    SlotStart = p.SlotStart.AddDays(dayDelta),
                    SlotEnd = p.SlotEnd.AddDays(dayDelta),
                    ImportPencePerKwh = p.ImportPencePerKwh,
                    ExportPencePerKwh = p.ExportPencePerKwh
                })
                .FirstOrDefault(p => p.SlotStart <= now && p.SlotEnd > now));
    }
}
