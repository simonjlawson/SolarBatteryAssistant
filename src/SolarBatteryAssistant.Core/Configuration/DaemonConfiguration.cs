namespace SolarBatteryAssistant.Core.Configuration;

/// <summary>
/// Root daemon configuration.
/// </summary>
public class DaemonConfiguration
{
    public const string SectionName = "SolarBatteryAssistant";

    public HomeAssistantConfiguration HomeAssistant { get; set; } = new();
    public BatteryConfiguration Battery { get; set; } = new();
    public SolarConfiguration Solar { get; set; } = new();
    public EnergyPricingConfiguration EnergyPricing { get; set; } = new();
    public PlanningConfiguration Planning { get; set; } = new();
    public StorageConfiguration Storage { get; set; } = new();
}
