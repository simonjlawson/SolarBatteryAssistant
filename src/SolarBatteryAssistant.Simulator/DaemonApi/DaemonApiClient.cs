using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SolarBatteryAssistant.Core.Interfaces;
using SolarBatteryAssistant.Core.Models;

namespace SolarBatteryAssistant.Simulator.DaemonApi;

/// <summary>
/// Connects to a locally-running SolarBatteryAssistant Daemon and reads
/// plans and battery status via the daemon's minimal REST API.
///
/// Implements:
///   - <see cref="IPlanRepository"/>  — browse and load stored plans
///   - <see cref="IBatteryStateProvider"/> — read current battery SoC
///   - <see cref="IEnergyPriceProvider"/>  — no-op (plans already embed prices)
/// </summary>
public class DaemonApiClient : IPlanRepository, IBatteryStateProvider, IDisposable
{
    private readonly HttpClient _http;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public DaemonApiClient(string daemonBaseUrl)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(daemonBaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    // -----------------------------------------------------------------------
    // IPlanRepository
    // -----------------------------------------------------------------------

    public async Task<EnergyPlan?> GetPlanAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.GetAsync($"api/plans/{date:yyyy-MM-dd}", cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<EnergyPlan>(JsonOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Failed to fetch plan for {date:yyyy-MM-dd} from daemon: {ex.Message}", ex);
        }
    }

    public async Task<IReadOnlyList<DateOnly>> GetAvailablePlanDatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var dates = await _http.GetFromJsonAsync<List<string>>("api/plans", JsonOptions, cancellationToken)
                        ?? [];
            return dates
                .Select(s => DateOnly.TryParseExact(s, "yyyy-MM-dd", out var d) ? (DateOnly?)d : null)
                .Where(d => d.HasValue)
                .Select(d => d!.Value)
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Failed to list plan dates from daemon: {ex.Message}", ex);
        }
    }

    public Task SavePlanAsync(EnergyPlan plan, CancellationToken cancellationToken = default)
        => Task.CompletedTask; // read-only client; daemon manages its own storage

    // -----------------------------------------------------------------------
    // IBatteryStateProvider
    // -----------------------------------------------------------------------

    public async Task<BatteryState> GetCurrentStateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await _http.GetFromJsonAsync<DaemonStatus>("api/status", JsonOptions, cancellationToken);
            return new BatteryState
            {
                ChargePercent = status?.BatteryChargePercent ?? 50.0,
                Timestamp = status?.BatteryTimestamp ?? DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Failed to read battery state from daemon: {ex.Message}", ex);
        }
    }

    // -----------------------------------------------------------------------
    // Connectivity check
    // -----------------------------------------------------------------------

    /// <summary>Returns true if the daemon is reachable.</summary>
    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.GetAsync("api/status", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _http.Dispose();
            _disposed = true;
        }
    }

    // -----------------------------------------------------------------------
    // Response models
    // -----------------------------------------------------------------------

    private class DaemonStatus
    {
        [JsonPropertyName("batteryChargePercent")]
        public double BatteryChargePercent { get; set; }

        [JsonPropertyName("batteryTimestamp")]
        public DateTimeOffset BatteryTimestamp { get; set; }

        [JsonPropertyName("currentAction")]
        public string? CurrentAction { get; set; }
    }
}
