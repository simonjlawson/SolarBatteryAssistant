using System.Globalization;

namespace SolarBatteryAssistant.Core.Planning;

/// <summary>
/// Helpers for loading and creating hourly profile CSV files used by the bruteforce planner.
///
/// CSV format: one numeric value per line, exactly 24 lines (no header), one per hour
/// starting at midnight (index 0 = 00:00–01:00, index 23 = 23:00–00:00).
/// </summary>
public static class CsvProfileLoader
{
    /// <summary>
    /// Reads a 24-row CSV of solar generation percentage weights.
    /// Each value represents the proportion of the day's total solar generation
    /// occurring in that hour.  Values need not sum to 100 — they are normalised
    /// before being returned so the caller can use them as fractional weights.
    /// </summary>
    /// <param name="path">Absolute or relative path to the CSV file.</param>
    /// <returns>Array of 24 normalised percentage weights (sum ≈ 100).</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when the file does not contain exactly 24 parseable numeric values.
    /// </exception>
    public static double[] LoadSolarHourlyPercentages(string path)
    {
        var raw = ReadDoublesFromFile(path, "solar hourly percentages");

        double total = raw.Sum();
        if (total <= 0)
            return raw; // all zeros — return as-is; callers handle this gracefully

        // Normalise so the weights sum to 100
        for (int i = 0; i < raw.Length; i++)
            raw[i] = raw[i] / total * 100.0;

        return raw;
    }

    /// <summary>
    /// Reads a 24-row CSV of extra house load values in Watts.
    /// Each value is added on top of the base load profile for the corresponding hour.
    /// Negative values are clamped to zero.
    /// </summary>
    /// <param name="path">Absolute or relative path to the CSV file.</param>
    /// <returns>Array of 24 non-negative Watt values.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when the file does not contain exactly 24 parseable numeric values.
    /// </exception>
    public static double[] LoadExtraLoadHourlyWatts(string path)
    {
        var raw = ReadDoublesFromFile(path, "extra load hourly watts");

        // Clamp negatives to zero
        for (int i = 0; i < raw.Length; i++)
            raw[i] = Math.Max(0, raw[i]);

        return raw;
    }

    /// <summary>
    /// Creates an example extra-load CSV at the specified path if it does not already exist.
    /// The example contains realistic household energy spikes (morning routine, lunch,
    /// afternoon, evening peak) suitable as a starting template.
    /// </summary>
    /// <param name="path">Path where the example file should be created.</param>
    public static void CreateExampleExtraLoadCsv(string path)
    {
        if (File.Exists(path))
            return;

        // 24 values (Watts), index = hour of day starting midnight
        // Peaks: breakfast 07-09, lunch 12-13, afternoon 15-16, dinner 17-20
        var exampleWatts = new double[]
        {
            0,    // 00:00
            0,    // 01:00
            0,    // 02:00
            0,    // 03:00
            0,    // 04:00
            0,    // 05:00
            50,   // 06:00 — early risers, kettle
            400,  // 07:00 — breakfast: toaster, kettle, microwave
            600,  // 08:00 — morning rush: shower heater, more cooking
            200,  // 09:00 — settling down, dishwasher running
            100,  // 10:00
            100,  // 11:00
            500,  // 12:00 — lunch: microwave, kettle
            300,  // 13:00 — post-lunch dishwasher
            100,  // 14:00
            200,  // 15:00 — afternoon, kids home
            200,  // 16:00
            600,  // 17:00 — dinner preparation begins
            800,  // 18:00 — peak cooking: oven, hob
            500,  // 19:00 — dinner, dishwasher
            300,  // 20:00 — evening, TV/devices
            150,  // 21:00
            50,   // 22:00
            0     // 23:00
        };

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllLines(path, exampleWatts.Select(w => w.ToString(CultureInfo.InvariantCulture)));
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static double[] ReadDoublesFromFile(string path, string contextName)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Profile CSV file not found: {path}", path);

        var lines = File.ReadAllLines(path)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l))
            .ToArray();

        if (lines.Length != 24)
            throw new InvalidDataException(
                $"Expected 24 values in {contextName} CSV '{path}', found {lines.Length}.");

        var values = new double[24];
        for (int i = 0; i < 24; i++)
        {
            if (!double.TryParse(lines[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                throw new InvalidDataException(
                    $"Could not parse value on line {i + 1} of {contextName} CSV '{path}': '{lines[i]}'");
        }

        return values;
    }
}
