using System.IO;
using System.Text.Json;
using SolarBatteryAssistant.Core.Models;

namespace SolarBatteryAssistant.Simulator.Rates;

/// <summary>
/// Persists and retrieves named <see cref="RateSet"/> objects as JSON files
/// under <c>%LocalAppData%\SolarBatteryAssistant\Rates\</c>.
/// </summary>
public static class RatesRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string RatesDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SolarBatteryAssistant", "Rates");

    /// <summary>Returns the names of all saved rate sets (newest first).</summary>
    public static IReadOnlyList<string> GetSavedRateSetNames()
    {
        var dir = RatesDirectory;
        if (!Directory.Exists(dir))
            return [];

        return Directory.GetFiles(dir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n != null)
            .Cast<string>()
            .OrderByDescending(n => n)
            .ToList();
    }

    /// <summary>Saves a rate set under <paramref name="name"/>.</summary>
    public static async Task SaveRateSetAsync(string name, IReadOnlyList<EnergyPrice> prices)
    {
        var dir = RatesDirectory;
        Directory.CreateDirectory(dir);

        var rateSet = new RateSet
        {
            Name = name,
            Date = prices.Count > 0
                ? DateOnly.FromDateTime(prices[0].SlotStart.Date)
                : DateOnly.FromDateTime(DateTime.Today),
            Prices = prices.ToList()
        };

        var json = JsonSerializer.Serialize(rateSet, JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(dir, $"{name}.json"), json);
    }

    /// <summary>Loads a previously saved rate set by name.</summary>
    public static async Task<RateSet?> LoadRateSetByNameAsync(string name)
        => await LoadRateSetFromFileAsync(Path.Combine(RatesDirectory, $"{name}.json"));

    /// <summary>Loads a rate set from an arbitrary file path.</summary>
    public static async Task<RateSet?> LoadRateSetFromFileAsync(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<RateSet>(json, JsonOptions);
        }
        catch (JsonException)
        {
            // File exists but contains invalid JSON — caller will display an error to the user
            return null;
        }
        catch (IOException)
        {
            // File access error (permissions, locked, etc.)
            return null;
        }
    }
}
