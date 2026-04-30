using SolarBatteryAssistant.Core.Models;

namespace SolarBatteryAssistant.Core.Interfaces;

/// <summary>
/// Controls battery behavior by activating HomeAssistant Scenes.
/// </summary>
public interface ISceneController
{
    /// <summary>
    /// Activates the HomeAssistant scene corresponding to the specified battery action.
    /// </summary>
    Task ActivateSceneAsync(BatteryAction action, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the currently active battery action/scene.
    /// </summary>
    Task<BatteryAction?> GetCurrentActionAsync(CancellationToken cancellationToken = default);
}
