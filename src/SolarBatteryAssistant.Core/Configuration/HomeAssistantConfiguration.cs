namespace SolarBatteryAssistant.Core.Configuration;

/// <summary>
/// Configuration for the HomeAssistant connection.
/// </summary>
public class HomeAssistantConfiguration
{
    /// <summary>Base URL of the HomeAssistant instance, e.g. http://homeassistant.local:8123</summary>
    public string BaseUrl { get; set; } = "http://homeassistant.local:8123";

    /// <summary>Long-lived access token for HomeAssistant API authentication.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Scene entity ID prefix. Scenes will be referenced as scene.{prefix}{scene_name}.
    /// E.g. "battery_" => scene.battery_import_from_grid
    /// Leave empty if scene names match exactly.
    /// </summary>
    public string ScenePrefix { get; set; } = string.Empty;

    /// <summary>
    /// Map of BatteryAction name to HA scene entity_id.
    /// Overrides auto-generated scene names.
    /// </summary>
    public Dictionary<string, string> SceneEntityIds { get; set; } = new()
    {
        ["ImportFromGrid"] = "scene.import_from_grid",
        ["ExportToGrid"] = "scene.export_to_grid",
        ["BypassBatteryOnlyUseGrid"] = "scene.bypass_battery_only_use_grid",
        ["NormalBatteryMinimiseGrid"] = "scene.normal_battery_minimise_grid"
    };

    /// <summary>HTTP request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;
}
