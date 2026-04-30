namespace SolarBatteryAssistant.Core.Configuration;

/// <summary>
/// Energy pricing source configuration.
/// </summary>
public class EnergyPricingConfiguration
{
    /// <summary>Pricing provider to use.</summary>
    public PricingProvider Provider { get; set; } = PricingProvider.OctopusAgile;

    /// <summary>Octopus Agile specific configuration.</summary>
    public OctopusConfiguration Octopus { get; set; } = new();
}

public enum PricingProvider
{
    /// <summary>Octopus Energy Agile tariff.</summary>
    OctopusAgile
}

/// <summary>
/// Configuration for the Octopus Energy Agile API.
/// </summary>
public class OctopusConfiguration
{
    /// <summary>
    /// Octopus Energy API key (optional for Agile public prices, required for account data).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Octopus Energy account number (e.g. A-B1C2D3EF).
    /// When set together with <see cref="ApiKey"/>, the provider will automatically
    /// retrieve the active import and export product codes from the Account API
    /// instead of using the manually configured values.
    /// </summary>
    public string? AccountNumber { get; set; }

    /// <summary>
    /// Agile tariff region code.
    /// Valid values: A (East England), B (East Midlands), C (London), D (North Wales),
    /// E (West Midlands), F (North East), G (North West), H (Southern), J (South East),
    /// K (South Wales), L (South West), M (Yorkshire), N (South Scotland), P (North Scotland)
    /// </summary>
    public string RegionCode { get; set; } = "C";

    /// <summary>
    /// Octopus Agile import product code. Defaults to current Agile product.
    /// Ignored when <see cref="AccountNumber"/> is provided and product codes are
    /// discovered automatically from the Account API.
    /// </summary>
    public string ImportProductCode { get; set; } = "AGILE-FLEX-22-11-25";

    /// <summary>
    /// Octopus Agile export product code. Leave empty if no export tariff.
    /// Ignored when <see cref="AccountNumber"/> is provided and product codes are
    /// discovered automatically from the Account API.
    /// </summary>
    public string? ExportProductCode { get; set; }

    /// <summary>
    /// Base URL for the Octopus API.
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.octopus.energy";

    /// <summary>
    /// How many days ahead of prices to cache.
    /// </summary>
    public int CacheDays { get; set; } = 2;
}
