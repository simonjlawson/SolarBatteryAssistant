using ImpSoft.OctopusEnergy.Api;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SolarBatteryAssistant.Core.Configuration;
using SolarBatteryAssistant.Core.Interfaces;
using SolarBatteryAssistant.Core.Models;

namespace SolarBatteryAssistant.Infrastructure.Providers;

/// <summary>
/// Fetches energy import/export prices from the Octopus Energy API using the
/// <see cref="IOctopusEnergyClient"/> from the ImpSoft.OctopusEnergy.Api package.
///
/// Tariff code format: E-1R-{product_code}-{region}
/// e.g. "E-1R-AGILE-FLEX-22-11-25-C"
///
/// When an account number and API key are configured the active tariff codes are
/// discovered automatically from the account's meter-point agreements via
/// <see cref="OctopusAccountService"/>. Otherwise the manually configured
/// <see cref="OctopusConfiguration.ImportProductCode"/> /
/// <see cref="OctopusConfiguration.ExportProductCode"/> values are used.
/// </summary>
public class OctopusAgilePriceProvider : IEnergyPriceProvider
{
    private readonly IOctopusEnergyClient _octopusClient;
    private readonly OctopusConfiguration _config;
    private readonly bool _enableCaching;
    private readonly OctopusAccountService _accountService;
    private readonly ILogger<OctopusAgilePriceProvider> _logger;

    // In-memory cache keyed by date (thread-safe)
    private readonly System.Collections.Concurrent.ConcurrentDictionary<DateOnly, List<EnergyPrice>> _cache = new();

    public OctopusAgilePriceProvider(
        IOctopusEnergyClient octopusClient,
        OctopusAccountService accountService,
        IOptions<DaemonConfiguration> config,
        ILogger<OctopusAgilePriceProvider> logger)
    {
        _octopusClient = octopusClient;
        _accountService = accountService;
        _config = config.Value.EnergyPricing.Octopus;
        _enableCaching = _config.EnableUnitRateCaching;
        _logger = logger;
    }

    public async Task<IReadOnlyList<EnergyPrice>> GetPricesForDateAsync(
        DateOnly date, CancellationToken cancellationToken = default)
    {
        if (_enableCaching && _cache.TryGetValue(date, out var cached))
            return cached;

        // Resolve tariff codes from the Account API on first use (no-op if already done or disabled)
        await _accountService.ResolveProductCodesAsync(cancellationToken);

        var (importProductCode, importTariffCode) = ResolveImportTariff();
        var (exportProductCode, exportTariffCode) = ResolveExportTariff();

        var importPrices = await FetchRatesAsync(
            importProductCode, importTariffCode, date, isExport: false, cancellationToken);

        if (!string.IsNullOrEmpty(exportProductCode) && !string.IsNullOrEmpty(exportTariffCode))
        {
            var exportRates = await FetchRatesAsync(
                exportProductCode, exportTariffCode, date, isExport: true, cancellationToken);

            var exportLookup = exportRates.ToDictionary(r => r.SlotStart, r => r.ImportPencePerKwh);

            foreach (var price in importPrices)
            {
                if (exportLookup.TryGetValue(price.SlotStart, out decimal exportPrice))
                    price.ExportPencePerKwh = exportPrice;
            }
        }

        if (_enableCaching)
            return _cache.GetOrAdd(date, importPrices);
        else
            return importPrices;
    }

    public async Task<EnergyPrice?> GetCurrentPriceAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var prices = await GetPricesForDateAsync(today, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        return prices.FirstOrDefault(p => p.SlotStart <= now && p.SlotEnd > now);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the import (product code, tariff code) pair to use.
    /// Account-discovered tariff takes priority; falls back to config.
    /// </summary>
    private (string productCode, string tariffCode) ResolveImportTariff()
    {
        if (!string.IsNullOrEmpty(_accountService.ImportTariffCode) &&
            !string.IsNullOrEmpty(_accountService.ImportProductCode))
        {
            return (_accountService.ImportProductCode!, _accountService.ImportTariffCode!);
        }

        // Fall back to manually configured values
        var productCode = _config.ImportProductCode;
        var tariffCode = $"E-1R-{productCode}-{_config.RegionCode}";
        return (productCode, tariffCode);
    }

    /// <summary>
    /// Returns the export (product code, tariff code) pair to use, or (null, null) if
    /// no export tariff is configured or discovered.
    /// </summary>
    private (string? productCode, string? tariffCode) ResolveExportTariff()
    {
        if (!string.IsNullOrEmpty(_accountService.ExportTariffCode) &&
            !string.IsNullOrEmpty(_accountService.ExportProductCode))
        {
            return (_accountService.ExportProductCode, _accountService.ExportTariffCode);
        }

        // Fall back to manually configured export product code (may be null/empty)
        if (string.IsNullOrEmpty(_config.ExportProductCode))
            return (null, null);

        var productCode = _config.ExportProductCode;
        var tariffCode = $"E-1R-{productCode}-{_config.RegionCode}";
        return (productCode, tariffCode);
    }

    private async Task<List<EnergyPrice>> FetchRatesAsync(
        string productCode, string tariffCode, DateOnly date, bool isExport, CancellationToken ct)
    {
        var periodFrom = new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);
        var periodTo = periodFrom.AddDays(1);

        _logger.LogDebug(
            "Fetching Octopus {Type} rates via ImpSoft client: product={ProductCode} tariff={TariffCode}",
            isExport ? "export" : "import", productCode, tariffCode);

        try
        {
            var charges = await _octopusClient.GetElectricityUnitRatesAsync(
                productCode, tariffCode, ElectricityUnitRate.Standard, periodFrom, periodTo);

            var results = charges
                .Select(c => new EnergyPrice
                {
                    SlotStart = c.ValidFromUTC,
                    SlotEnd = c.ValidToUTC,
                    ImportPencePerKwh = c.ValueIncludingVAT
                })
                .OrderBy(p => p.SlotStart)
                .ToList();

            if (results.Count == 0)
                _logger.LogWarning(
                    "No Octopus {Type} rate results returned for {Date} (product={ProductCode}, tariff={TariffCode})",
                    isExport ? "export" : "import", date, productCode, tariffCode);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error fetching Octopus {Type} rates for {Date} (product={ProductCode}, tariff={TariffCode})",
                isExport ? "export" : "import", date, productCode, tariffCode);
            return [];
        }
    }
}
