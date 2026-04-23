namespace SolarBatteryAssistant.Core.Configuration;

/// <summary>
/// Storage configuration for persisting plans.
/// </summary>
public class StorageConfiguration
{
    /// <summary>
    /// Directory path for storing plan JSON files.
    /// Supports environment variable expansion, e.g. %APPDATA% or $HOME.
    /// </summary>
    public string DataDirectory { get; set; } = "./data/plans";

    /// <summary>
    /// Number of days of historical plans to retain.
    /// </summary>
    public int RetentionDays { get; set; } = 90;
}
