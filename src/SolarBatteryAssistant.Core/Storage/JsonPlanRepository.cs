using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SolarBatteryAssistant.Core.Configuration;
using SolarBatteryAssistant.Core.Interfaces;
using SolarBatteryAssistant.Core.Models;

namespace SolarBatteryAssistant.Core.Storage;

/// <summary>
/// Stores and retrieves energy plans as JSON files on disk.
/// One file per day, named YYYY-MM-DD.json.
/// </summary>
public class JsonPlanRepository : IPlanRepository
{
    private readonly StorageConfiguration _config;
    private readonly ILogger<JsonPlanRepository> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public JsonPlanRepository(IOptions<DaemonConfiguration> config, ILogger<JsonPlanRepository> logger)
    {
        _config = config.Value.Storage;
        _logger = logger;
    }

    public async Task SavePlanAsync(EnergyPlan plan, CancellationToken cancellationToken = default)
    {
        var dir = ResolveDataDirectory();
        Directory.CreateDirectory(dir);

        var filePath = PlanFilePath(dir, plan.PlanDate);
        var json = JsonSerializer.Serialize(plan, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);

        _logger.LogDebug("Saved plan for {Date} to {File}", plan.PlanDate, filePath);

        await PurgeOldPlansAsync(dir, cancellationToken);
    }

    public async Task<EnergyPlan?> GetPlanAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var dir = ResolveDataDirectory();
        var filePath = PlanFilePath(dir, date);

        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            return JsonSerializer.Deserialize<EnergyPlan>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read plan for {Date} from {File}", date, filePath);
            return null;
        }
    }

    public Task<IReadOnlyList<DateOnly>> GetAvailablePlanDatesAsync(CancellationToken cancellationToken = default)
    {
        var dir = ResolveDataDirectory();
        if (!Directory.Exists(dir))
            return Task.FromResult<IReadOnlyList<DateOnly>>(Array.Empty<DateOnly>());

        var dates = Directory.GetFiles(dir, "????-??-??.json")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Select(name => DateOnly.TryParseExact(name, "yyyy-MM-dd", out var d) ? (DateOnly?)d : null)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .OrderByDescending(d => d)
            .ToList();

        return Task.FromResult<IReadOnlyList<DateOnly>>(dates);
    }

    private string ResolveDataDirectory()
    {
        var path = _config.DataDirectory;
        // Expand environment variables
        path = Environment.ExpandEnvironmentVariables(path);
        if (!Path.IsPathRooted(path))
            path = Path.Combine(AppContext.BaseDirectory, path);
        return path;
    }

    private static string PlanFilePath(string dir, DateOnly date)
        => Path.Combine(dir, $"{date:yyyy-MM-dd}.json");

    private Task PurgeOldPlansAsync(string dir, CancellationToken cancellationToken)
    {
        if (_config.RetentionDays <= 0) return Task.CompletedTask;

        var cutoff = DateOnly.FromDateTime(DateTime.Today).AddDays(-_config.RetentionDays);
        var old = Directory.GetFiles(dir, "????-??-??.json")
            .Where(f =>
            {
                var name = Path.GetFileNameWithoutExtension(f);
                return DateOnly.TryParseExact(name, "yyyy-MM-dd", out var d) && d < cutoff;
            });

        foreach (var file in old)
        {
            try { File.Delete(file); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete old plan file {File}", file);
            }
        }

        return Task.CompletedTask;
    }
}
