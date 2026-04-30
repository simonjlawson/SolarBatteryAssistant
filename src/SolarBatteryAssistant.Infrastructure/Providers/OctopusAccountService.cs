using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SolarBatteryAssistant.Core.Configuration;

namespace SolarBatteryAssistant.Infrastructure.Providers;

/// <summary>
/// Retrieves the active electricity product codes (import and export) for an
/// Octopus Energy account using the Account API.
///
/// API documentation:
/// https://developer.octopus.energy/rest/reference/retail/#accounts-retrieve
///
/// Endpoint: GET /v1/accounts/{account_number}/
/// Authentication: HTTP Basic Auth with API key as username and an empty password.
/// </summary>
public class OctopusAccountService
{
    private readonly HttpClient _http;
    private readonly OctopusConfiguration _config;
    private readonly ILogger<OctopusAccountService> _logger;

    // Lazily resolved codes. Null until first successful account lookup.
    private string? _importProductCode;
    private string? _exportProductCode;
    private bool _lookupAttempted;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public OctopusAccountService(
        HttpClient http,
        IOptions<DaemonConfiguration> config,
        ILogger<OctopusAccountService> logger)
    {
        _http = http;
        _config = config.Value.EnergyPricing.Octopus;
        _logger = logger;

        _http.BaseAddress = new Uri(_config.BaseUrl.TrimEnd('/') + "/");

        if (!string.IsNullOrEmpty(_config.ApiKey))
        {
            var credentials = Convert.ToBase64String(
                System.Text.Encoding.ASCII.GetBytes(_config.ApiKey + ":"));
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
        }
    }

    /// <summary>
    /// Returns whether auto-discovery via the Account API is enabled
    /// (i.e. both <see cref="OctopusConfiguration.AccountNumber"/> and
    /// <see cref="OctopusConfiguration.ApiKey"/> are configured).
    /// </summary>
    public bool IsEnabled =>
        !string.IsNullOrEmpty(_config.AccountNumber) &&
        !string.IsNullOrEmpty(_config.ApiKey);

    /// <summary>
    /// Lazily fetches the account and resolves the active import and export
    /// product codes. Results are cached in memory for the lifetime of this
    /// service instance. Call this once before using the product codes.
    /// </summary>
    public async Task ResolveProductCodesAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabled) return;

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (_lookupAttempted) return;
            _lookupAttempted = true;

            _logger.LogInformation(
                "Fetching Octopus account data for account {AccountNumber} to auto-discover product codes.",
                _config.AccountNumber);

            var url = $"v1/accounts/{Uri.EscapeDataString(_config.AccountNumber!)}/";
            var account = await _http.GetFromJsonAsync<OctopusAccountResponse>(url, cancellationToken);

            if (account?.Properties == null || account.Properties.Count == 0)
            {
                _logger.LogWarning("No properties found in Octopus account response.");
                return;
            }

            // We may have multiple properties/meter points. Choose the import/export
            // product code whose tariff indicates the newest date (e.g. AGILE-FLEX-22-11-25)
            // If no date can be parsed from the product code, fall back to the
            // agreement valid_to/valid_from timestamps.
            (string? code, DateTimeOffset key) bestImport = (null, DateTimeOffset.MinValue);
            (string? code, DateTimeOffset key) bestExport = (null, DateTimeOffset.MinValue);

            foreach (var property in account.Properties)
            {
                foreach (var meterPoint in property.ElectricityMeterPoints ?? [])
                {
                    var activeAgreement = meterPoint.Agreements?
                        .OrderByDescending(a => a.ValidTo ?? DateTimeOffset.MaxValue)
                        .FirstOrDefault();

                    if (activeAgreement?.TariffCode == null) continue;

                    var productCode = ExtractProductCode(activeAgreement.TariffCode);
                    if (productCode == null) continue;

                    // Determine a comparison key: prefer a date embedded in the product code
                    // (format yy-MM-dd or yyyy-MM-dd). If not present, use agreement valid_to
                    // or valid_from.
                    DateTimeOffset key;
                    var parsedDate = ExtractDateFromProductCode(productCode);
                    if (parsedDate.HasValue)
                    {
                        // Treat parsed date as UTC midnight for comparison
                        key = parsedDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                    }
                    else if (activeAgreement.ValidTo.HasValue)
                    {
                        key = activeAgreement.ValidTo.Value;
                    }
                    else
                    {
                        key = activeAgreement.ValidFrom;
                    }

                    if (meterPoint.IsExport)
                    {
                        if (bestExport.code == null || key > bestExport.key)
                        {
                            bestExport = (productCode, key);
                            _logger.LogInformation(
                                "Auto-discovered export product code candidate: {ProductCode} (from tariff {TariffCode}, key {Key})",
                                productCode, activeAgreement.TariffCode, key);
                        }
                    }
                    else
                    {
                        if (bestImport.code == null || key > bestImport.key)
                        {
                            bestImport = (productCode, key);
                            _logger.LogInformation(
                                "Auto-discovered import product code candidate: {ProductCode} (from tariff {TariffCode}, key {Key})",
                                productCode, activeAgreement.TariffCode, key);
                        }
                    }
                }
            }

            if (bestImport.code != null)
            {
                _importProductCode = bestImport.code;
                _logger.LogInformation("Selected import product code: {ProductCode}", _importProductCode);
            }

            if (bestExport.code != null)
            {
                _exportProductCode = bestExport.code;
                _logger.LogInformation("Selected export product code: {ProductCode}", _exportProductCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve Octopus account data. Will fall back to configured product codes.");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Returns the resolved import product code, or null if not yet resolved.
    /// </summary>
    public string? ImportProductCode => _importProductCode;

    /// <summary>
    /// Returns the resolved export product code, or null if not yet resolved.
    /// </summary>
    public string? ExportProductCode => _exportProductCode;

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Extracts the product code from an Octopus tariff code.
    /// Tariff code format: {fuel}-{rate_type}-{product_code}-{region}
    /// Example: "E-1R-AGILE-FLEX-22-11-25-C" → "AGILE-FLEX-22-11-25"
    /// </summary>
    internal static string? ExtractProductCode(string tariffCode)
    {
        // Format: E-1R-{product_code}-{single_char_region}
        // Split into parts and strip the first two segments (fuel + rate type)
        // and the last segment (single-char region code).
        var parts = tariffCode.Split('-');
        if (parts.Length < 4) return null;

        // Skip first two ("E", "1R") and last one (region char)
        return string.Join("-", parts[2..^1]);
    }

    private static DateOnly? ExtractDateFromProductCode(string productCode)
    {
        // Product code typically ends with a date like 22-11-25 (yy-MM-dd) or 2022-11-25 (yyyy-MM-dd).
        var parts = productCode.Split('-');
        if (parts.Length < 3) return null;

        // Try last three parts as yy-MM-dd or yyyy-MM-dd
        var lastThree = string.Join('-', parts[^3..]);

        if (DateOnly.TryParseExact(lastThree, "yy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var d1))
            return d1;
        if (DateOnly.TryParseExact(lastThree, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var d2))
            return d2;

        return null;
    }

    // -------------------------------------------------------------------------
    // Octopus Account API response models
    // -------------------------------------------------------------------------

    private class OctopusAccountResponse
    {
        [JsonPropertyName("number")]
        public string? Number { get; set; }

        [JsonPropertyName("properties")]
        public List<OctopusProperty>? Properties { get; set; }
    }

    private class OctopusProperty
    {
        [JsonPropertyName("electricity_meter_points")]
        public List<OctopusMeterPoint>? ElectricityMeterPoints { get; set; }
    }

    private class OctopusMeterPoint
    {
        [JsonPropertyName("mpan")]
        public string? Mpan { get; set; }

        [JsonPropertyName("is_export")]
        public bool IsExport { get; set; }

        [JsonPropertyName("agreements")]
        public List<OctopusAgreement>? Agreements { get; set; }
    }

    private class OctopusAgreement
    {
        [JsonPropertyName("tariff_code")]
        public string? TariffCode { get; set; }

        [JsonPropertyName("valid_from")]
        public DateTimeOffset ValidFrom { get; set; }

        [JsonPropertyName("valid_to")]
        public DateTimeOffset? ValidTo { get; set; }
    }
}
