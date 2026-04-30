using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SolarBatteryAssistant.Core.Configuration;
using SolarBatteryAssistant.Core.Interfaces;
using SolarBatteryAssistant.Core.Models;
using SolarBatteryAssistant.Infrastructure.HomeAssistant;

namespace SolarBatteryAssistant.Infrastructure.Providers;

/// <summary>
/// Reads the battery state of charge from a HomeAssistant sensor entity.
/// </summary>
public class HomeAssistantBatteryStateProvider : IBatteryStateProvider
{
    private readonly HomeAssistantClient _haClient;
    private readonly BatteryConfiguration _config;
    private readonly ILogger<HomeAssistantBatteryStateProvider> _logger;

    public HomeAssistantBatteryStateProvider(
        HomeAssistantClient haClient,
        IOptions<DaemonConfiguration> config,
        ILogger<HomeAssistantBatteryStateProvider> logger)
    {
        _haClient = haClient;
        _config = config.Value.Battery;
        _logger = logger;
    }

    public async Task<BatteryState> GetCurrentStateAsync(CancellationToken cancellationToken = default)
    {
        var entityState = await _haClient.GetEntityStateAsync(_config.SocEntityId, cancellationToken);

        if (entityState == null)
        {
            _logger.LogWarning("Could not read battery SoC entity {Entity}. Defaulting to 50%.", _config.SocEntityId);
            return new BatteryState { ChargePercent = 50, Timestamp = DateTimeOffset.UtcNow };
        }

        if (!double.TryParse(entityState.State, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double soc))
        {
            _logger.LogWarning("Battery SoC entity {Entity} has non-numeric state '{State}'. Defaulting to 50%.",
                _config.SocEntityId, entityState.State);
            return new BatteryState { ChargePercent = 50, Timestamp = DateTimeOffset.UtcNow };
        }

        _logger.LogDebug("Battery SoC: {Soc}%", soc);
        return new BatteryState
        {
            ChargePercent = Math.Clamp(soc, 0, 100),
            Timestamp = entityState.LastUpdated
        };
    }
}
