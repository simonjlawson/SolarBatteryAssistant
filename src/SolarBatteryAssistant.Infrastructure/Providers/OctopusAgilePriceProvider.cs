using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SolarBatteryAssistant.Core.Configuration;
using SolarBatteryAssistant.Core.Interfaces;
using SolarBatteryAssistant.Core.Models;

namespace SolarBatteryAssistant.Infrastructure.Providers;

/// <summary>
/// Fetches energy import/export prices from the Octopus Energy Agile API.
///
/// API documentation:
/// https://developer.octopus.energy/guides/rest/api-endpoints/#agile-prices
///
/// Price endpoint format:
/// GET /v1/products/{product_code}/electricity-tariffs/E-1R-{product_code}-{region}/standard-unit-rates/
/// </summary>
public class OctopusAgilePriceProvider : IEnergyPriceProvider
{
    private readonly HttpClient _http;
    private readonly OctopusConfiguration _config;
    private readonly OctopusAccountService _accountService;
    private readonly ILogger<OctopusAgilePriceProvider> _logger;

    // In-memory cache keyed by date (thread-safe)
    private readonly System.Collections.Concurrent.ConcurrentDictionary<DateOnly, List<EnergyPrice>> _cache = new();

    public OctopusAgilePriceProvider(
        HttpClient http,
        OctopusAccountService accountService,
        IOptions<DaemonConfiguration> config,
        ILogger<OctopusAgilePriceProvider> logger)
    {
        _http = http;
        _accountService = accountService;
        _config = config.Value.EnergyPricing.Octopus;
        _logger = logger;

        _http.BaseAddress = new Uri(_config.BaseUrl.TrimEnd('/') + "/");

        // Set basic auth if API key configured
        if (!string.IsNullOrEmpty(_config.ApiKey))
        {
            var credentials = Convert.ToBase64String(
                System.Text.Encoding.ASCII.GetBytes(_config.ApiKey + ":"));
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
        }
    }

    public async Task<IReadOnlyList<EnergyPrice>> GetPricesForDateAsync(
        DateOnly date, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(date, out var cached))
            return cached;

        // Resolve product codes from the Account API on first use (no-op if already done or disabled)
        await _accountService.ResolveProductCodesAsync(cancellationToken);

        var importProductCode = _accountService.ImportProductCode ?? _config.ImportProductCode;
        var exportProductCode = _accountService.ExportProductCode ?? _config.ExportProductCode;

        var importPrices = await FetchRatesAsync(
            importProductCode, date, isExport: false, cancellationToken);

        Dictionary<DateTimeOffset, decimal> exportLookup = new();
        if (!string.IsNullOrEmpty(exportProductCode))
        {
            var exportRates = await FetchRatesAsync(
                exportProductCode, date, isExport: true, cancellationToken);
            exportLookup = exportRates
                .ToDictionary(r => r.SlotStart, r => r.ImportPencePerKwh);
        }

            foreach (var price in importPrices)
            {
                if (exportLookup.TryGetValue(price.SlotStart, out decimal exportPrice))
                    // Matched variable Export price with Import variable price
                    price.ExportPencePerKwh = exportPrice;
                else
                    // Must be a static value always use singel value
                    price.ExportPencePerKwh = exportLookup.FirstOrDefault().Value;
            }
        }

        // Use GetOrAdd to avoid race conditions when adding to the cache
        return _cache.GetOrAdd(date, importPrices);
    }

    public async Task<EnergyPrice?> GetCurrentPriceAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var prices = await GetPricesForDateAsync(today, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        return prices.FirstOrDefault(p => p.SlotStart <= now && p.SlotEnd > now);
    }

    // -------------------------------------------------------------------------

    private async Task<List<EnergyPrice>> FetchRatesAsync(
        string productCode, DateOnly date, bool isExport, CancellationToken ct)
    {
        // Tariff code format: E-1R-{PRODUCT_CODE}-{REGION}
        var tariffCode = $"E-1R-{productCode}-{_config.RegionCode}";
        var periodFrom = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).ToString("o");
        var periodTo = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).ToString("o");

        var url = $"v1/products/{productCode}/electricity-tariffs/{tariffCode}/standard-unit-rates/" +
                  $"?period_from={Uri.EscapeDataString(periodFrom)}" +
                  $"&period_to={Uri.EscapeDataString(periodTo)}" +
                  $"&page_size=100";

        _logger.LogDebug("Fetching Octopus {Type} rates: {Url}", isExport ? "export" : "import", url);

        try
        {
            var response = await _http.GetFromJsonAsync<OctopusRatesResponse>(url, ct);
            if (response?.Results == null)
            {
                _logger.LogWarning("No Octopus rate results returned for {Date}", date);
                return new List<EnergyPrice>();
            }

            return response.Results
                .Select(r => new EnergyPrice
                {
                    SlotStart = r.ValidFrom,
                    SlotEnd = r.ValidTo,
                    ImportPencePerKwh = (decimal)r.ValueIncVat
                })
                .OrderBy(p => p.SlotStart)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Octopus {Type} rates for {Date}", isExport ? "export" : "import", date);
            return new List<EnergyPrice>();
        }
    }

    // -------------------------------------------------------------------------
    // Octopus API response models
    // -------------------------------------------------------------------------

    private class OctopusRatesResponse
    {
        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("results")]
        public List<OctopusRateResult>? Results { get; set; }
    }

    private class OctopusRateResult
    {
        [JsonPropertyName("value_exc_vat")]
        public double ValueExcVat { get; set; }

        [JsonPropertyName("value_inc_vat")]
        public double ValueIncVat { get; set; }

        [JsonPropertyName("valid_from")]
        public DateTimeOffset ValidFrom { get; set; }

        [JsonPropertyName("valid_to")]
        public DateTimeOffset ValidTo { get; set; }
    }
}
