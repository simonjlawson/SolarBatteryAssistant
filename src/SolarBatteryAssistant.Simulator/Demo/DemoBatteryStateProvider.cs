using SolarBatteryAssistant.Core.Interfaces;
using SolarBatteryAssistant.Core.Models;

namespace SolarBatteryAssistant.Simulator.Demo;

/// <summary>
/// Returns a fixed configurable battery state of charge for demo/offline use.
/// </summary>
public class DemoBatteryStateProvider : IBatteryStateProvider
{
    /// <summary>Starting battery state of charge percentage (0–100).</summary>
    public double ChargePercent { get; set; } = 50.0;

    public Task<BatteryState> GetCurrentStateAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new BatteryState
        {
            ChargePercent = ChargePercent,
            Timestamp = DateTimeOffset.UtcNow
        });
    }
}
