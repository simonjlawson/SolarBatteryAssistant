using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SolarBatteryAssistant.Core.Configuration;

namespace SolarBatteryAssistant.Infrastructure.Providers;

/// <summary>
/// Retrieves the active electricity tariff codes for an Octopus Energy account
/// using the Account API.
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
    private string? _importTariffCode;
    private string? _exportTariffCode;
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
    /// Lazily fetches the account and resolves the currently-active import and
    /// export tariff codes from the account's meter-point agreements.
    /// Results are cached in memory for the lifetime of this service instance.
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
                "Fetching Octopus account data for account {AccountNumber} to discover active tariffs.",
                _config.AccountNumber);

            var url = $"v1/accounts/{Uri.EscapeDataString(_config.AccountNumber!)}/";
            var account = await _http.GetFromJsonAsync<OctopusAccountResponse>(url, cancellationToken);

            if (account?.Properties == null || account.Properties.Count == 0)
            {
                _logger.LogWarning("No properties found in Octopus account response.");
                return;
            }

            var now = DateTimeOffset.UtcNow;

            foreach (var property in account.Properties)
            {
                foreach (var meterPoint in property.ElectricityMeterPoints ?? [])
                {
                    // Find the agreement that is currently active:
                    //   valid_from <= now  AND  (valid_to is null  OR  valid_to > now)
                    var activeAgreement = meterPoint.Agreements?
                        .Where(a => a.ValidFrom <= now &&
                                    (!a.ValidTo.HasValue || a.ValidTo.Value > now))
                        .OrderByDescending(a => a.ValidFrom) // most-recently-started wins if multiple match
                        .FirstOrDefault();

                    if (activeAgreement?.TariffCode == null)
                    {
                        _logger.LogDebug(
                            "No currently-active agreement found for {MeterType} meter point {Mpan}.",
                            meterPoint.IsExport ? "export" : "import",
                            meterPoint.Mpan);
                        continue;
                    }

                    if (meterPoint.IsExport)
                    {
                        if (_exportTariffCode == null)
                        {
                            _exportTariffCode = activeAgreement.TariffCode;
                            _logger.LogInformation(
                                "Resolved active export tariff: {TariffCode} (MPAN {Mpan}, valid from {From})",
                                _exportTariffCode, meterPoint.Mpan, activeAgreement.ValidFrom);
                        }
                    }
                    else
                    {
                        if (_importTariffCode == null)
                        {
                            _importTariffCode = activeAgreement.TariffCode;
                            _logger.LogInformation(
                                "Resolved active import tariff: {TariffCode} (MPAN {Mpan}, valid from {From})",
                                _importTariffCode, meterPoint.Mpan, activeAgreement.ValidFrom);
                        }
                    }
                }
            }

            if (_importTariffCode == null)
                _logger.LogWarning("Could not resolve an active import tariff from the account. " +
                                   "Will fall back to configured product code.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to retrieve Octopus account data. Will fall back to configured product codes.");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// The currently-active import tariff code resolved from the account
    /// (e.g. "E-1R-AGILE-FLEX-22-11-25-C"), or <c>null</c> if not yet resolved.
    /// </summary>
    public string? ImportTariffCode => _importTariffCode;

    /// <summary>
    /// The currently-active export tariff code resolved from the account
    /// (e.g. "E-1R-OUTGOING-AGILE-22-11-25-C"), or <c>null</c> if not yet resolved.
    /// </summary>
    public string? ExportTariffCode => _exportTariffCode;

    /// <summary>
    /// The product code extracted from <see cref="ImportTariffCode"/>, or <c>null</c>.
    /// </summary>
    public string? ImportProductCode =>
        _importTariffCode != null ? ExtractProductCode(_importTariffCode) : null;

    /// <summary>
    /// The product code extracted from <see cref="ExportTariffCode"/>, or <c>null</c>.
    /// </summary>
    public string? ExportProductCode =>
        _exportTariffCode != null ? ExtractProductCode(_exportTariffCode) : null;

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Extracts the product code from an Octopus tariff code.
    /// Tariff code format: {fuel}-{rate_type}-{product_code}-{single_char_region}
    /// Example: "E-1R-AGILE-FLEX-22-11-25-C" → "AGILE-FLEX-22-11-25"
    /// </summary>
    internal static string? ExtractProductCode(string tariffCode)
    {
        var parts = tariffCode.Split('-');
        if (parts.Length < 4) return null;

        // Skip first two segments ("E", "1R") and last segment (single-char region code)
        return string.Join("-", parts[2..^1]);
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
