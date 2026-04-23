using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SolarBatteryAssistant.Core.Configuration;

namespace SolarBatteryAssistant.Infrastructure.HomeAssistant;

/// <summary>
/// Client for the HomeAssistant REST API.
/// Docs: https://developers.home-assistant.io/docs/api/rest/
/// </summary>
public class HomeAssistantClient
{
    private readonly HttpClient _http;
    private readonly HomeAssistantConfiguration _config;
    private readonly ILogger<HomeAssistantClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public HomeAssistantClient(
        HttpClient http,
        IOptions<DaemonConfiguration> config,
        ILogger<HomeAssistantClient> logger)
    {
        _http = http;
        _config = config.Value.HomeAssistant;
        _logger = logger;

        _http.BaseAddress = new Uri(_config.BaseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _config.AccessToken);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _http.Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds);
    }

    /// <summary>Gets the state of a HomeAssistant entity.</summary>
    public async Task<EntityState?> GetEntityStateAsync(string entityId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync($"api/states/{entityId}", ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("HA returned {Status} for entity {Entity}", response.StatusCode, entityId);
                return null;
            }
            return await response.Content.ReadFromJsonAsync<EntityState>(JsonOptions, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching entity state for {Entity}", entityId);
            return null;
        }
    }

    /// <summary>Activates a HomeAssistant scene by entity_id.</summary>
    public async Task<bool> ActivateSceneAsync(string sceneEntityId, CancellationToken ct = default)
    {
        try
        {
            var payload = new { entity_id = sceneEntityId };
            var response = await _http.PostAsJsonAsync("api/services/scene/turn_on", payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("HA returned {Status} activating scene {Scene}", response.StatusCode, sceneEntityId);
                return false;
            }
            _logger.LogInformation("Activated scene {Scene}", sceneEntityId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error activating scene {Scene}", sceneEntityId);
            return false;
        }
    }

    /// <summary>Calls a HomeAssistant service.</summary>
    public async Task<bool> CallServiceAsync(string domain, string service, object payload, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"api/services/{domain}/{service}", payload, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling service {Domain}.{Service}", domain, service);
            return false;
        }
    }

    /// <summary>Checks if the HomeAssistant API is reachable.</summary>
    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("api/", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>HomeAssistant entity state response.</summary>
public class EntityState
{
    [JsonPropertyName("entity_id")]
    public string EntityId { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("attributes")]
    public Dictionary<string, JsonElement> Attributes { get; set; } = new();

    [JsonPropertyName("last_changed")]
    public DateTimeOffset LastChanged { get; set; }

    [JsonPropertyName("last_updated")]
    public DateTimeOffset LastUpdated { get; set; }
}
