namespace SectorTelemetry;

/// <summary>Where the app keeps data it writes at runtime (learned track maps, settings).</summary>
public static class AppPaths
{
    // Per-user app data (e.g. %LOCALAPPDATA%\SectorTelemetry on Windows), since the executable's
    // own folder may be read-only or, for a single-file build, a temporary extraction directory.
    public static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SectorTelemetry");
}
