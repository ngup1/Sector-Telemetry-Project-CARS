using System.Text.Json;
using System.Text.Json.Serialization;

namespace SectorTelemetry.Services;

public sealed record DashboardSettings(
    [property: JsonPropertyName("speedUnit")] string SpeedUnit = "kph",
    [property: JsonPropertyName("tempUnit")] string TempUnit = "c");

/// <summary>A partial update: only non-null fields are applied.</summary>
public sealed record SettingsPatch(
    [property: JsonPropertyName("speedUnit")] string? SpeedUnit,
    [property: JsonPropertyName("tempUnit")] string? TempUnit);

/// <summary>
/// Dashboard display settings, stored in settings.json beside the executable so they apply to every
/// device viewing the dashboard and survive browser data being cleared.
/// </summary>
public sealed class SettingsStore(ILogger<SettingsStore> logger)
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "settings.json");
    private static readonly string[] SpeedUnits = ["kph", "mph"];
    private static readonly string[] TempUnits = ["c", "f"];

    private readonly object _lock = new();
    private DashboardSettings? _current;

    public DashboardSettings Get()
    {
        lock (_lock) return _current ??= Load();
    }

    /// <summary>Applies only the fields that are present and valid; returns the resulting settings.</summary>
    public DashboardSettings Update(SettingsPatch patch)
    {
        lock (_lock)
        {
            var s = _current ??= Load();
            s = s with
            {
                SpeedUnit = patch.SpeedUnit is { } su && SpeedUnits.Contains(su) ? su : s.SpeedUnit,
                TempUnit = patch.TempUnit is { } tu && TempUnits.Contains(tu) ? tu : s.TempUnit,
            };
            _current = s;
            try
            {
                File.WriteAllText(FilePath, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not save settings to {Path}", FilePath);
            }
            return s;
        }
    }

    private DashboardSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<DashboardSettings>(File.ReadAllText(FilePath)) ?? new DashboardSettings();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read settings from {Path}; using defaults", FilePath);
        }
        return new DashboardSettings();
    }
}
