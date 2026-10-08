using SectorTelemetry.Ams2;

namespace SectorTelemetry.Sources;

public interface ITelemetrySource : IDisposable
{
    /// <summary>Short human-readable status, e.g. "Connected" or "Waiting for AMS2".</summary>
    string Status { get; }

    /// <summary>Returns a new frame, or null if the game is unavailable or nothing changed since the last read.</summary>
    Ams2Frame? TryRead();
}
