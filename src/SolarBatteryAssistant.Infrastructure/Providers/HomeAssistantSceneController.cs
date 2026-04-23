using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SolarBatteryAssistant.Core.Configuration;
using SolarBatteryAssistant.Core.Interfaces;
using SolarBatteryAssistant.Core.Models;
using SolarBatteryAssistant.Infrastructure.HomeAssistant;

namespace SolarBatteryAssistant.Infrastructure.Providers;

/// <summary>
/// Controls battery behavior by activating HomeAssistant scenes via the REST API.
/// </summary>
public class HomeAssistantSceneController : ISceneController
{
    private readonly HomeAssistantClient _haClient;
    private readonly HomeAssistantConfiguration _config;
    private readonly ILogger<HomeAssistantSceneController> _logger;

    private BatteryAction? _lastActivatedAction;

    public HomeAssistantSceneController(
        HomeAssistantClient haClient,
        IOptions<DaemonConfiguration> config,
        ILogger<HomeAssistantSceneController> logger)
    {
        _haClient = haClient;
        _config = config.Value.HomeAssistant;
        _logger = logger;
    }

    public async Task ActivateSceneAsync(BatteryAction action, CancellationToken cancellationToken = default)
    {
        var sceneEntityId = ResolveSceneEntityId(action);

        _logger.LogInformation("Activating scene {Scene} for action {Action}", sceneEntityId, action);

        var success = await _haClient.ActivateSceneAsync(sceneEntityId, cancellationToken);

        if (success)
        {
            _lastActivatedAction = action;
            _logger.LogInformation("Scene {Scene} activated successfully", sceneEntityId);
        }
        else
        {
            _logger.LogError("Failed to activate scene {Scene}", sceneEntityId);
        }
    }

    public Task<BatteryAction?> GetCurrentActionAsync(CancellationToken cancellationToken = default)
    {
        // HA scenes don't have a queryable "current" state (they are one-shot activations).
        // We track the last action we set in memory.
        return Task.FromResult(_lastActivatedAction);
    }

    private string ResolveSceneEntityId(BatteryAction action)
    {
        var actionName = action.ToSceneName();
        if (_config.SceneEntityIds.TryGetValue(actionName, out var entityId))
            return entityId;

        // Fall back to auto-generated snake_case scene entity id
        var snake = ToSnakeCase(actionName);
        return $"scene.{_config.ScenePrefix}{snake}";
    }

    private static string ToSnakeCase(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        var result = new System.Text.StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            if (i > 0 && char.IsUpper(value[i]) && !char.IsUpper(value[i - 1]))
                result.Append('_');
            result.Append(char.ToLower(value[i]));
        }
        return result.ToString();
    }
}
