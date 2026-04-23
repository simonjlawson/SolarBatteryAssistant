namespace SolarBatteryAssistant.Core.Models;

/// <summary>
/// The action the battery system should take during a time slot.
/// Each action corresponds to a HomeAssistant Scene.
/// </summary>
public enum BatteryAction
{
    /// <summary>Normal operation: use solar and battery to minimise grid import.</summary>
    NormalBatteryMinimiseGrid,

    /// <summary>Force-charge battery from the grid.</summary>
    ImportFromGrid,

    /// <summary>Force-discharge battery and export to grid.</summary>
    ExportToGrid,

    /// <summary>Bypass battery: use grid only, preserve battery charge.</summary>
    BypassBatteryOnlyUseGrid
}

public static class BatteryActionExtensions
{
    /// <summary>Returns the HomeAssistant scene name for this action.</summary>
    public static string ToSceneName(this BatteryAction action) => action switch
    {
        BatteryAction.ImportFromGrid => "ImportFromGrid",
        BatteryAction.ExportToGrid => "ExportToGrid",
        BatteryAction.BypassBatteryOnlyUseGrid => "BypassBatteryOnlyUseGrid",
        BatteryAction.NormalBatteryMinimiseGrid => "NormalBatteryMinimiseGrid",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
}
