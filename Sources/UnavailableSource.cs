using SectorTelemetry.Ams2;

namespace SectorTelemetry.Sources;

/// <summary>Used where AMS2's shared memory can't exist (anything but Windows).</summary>
public sealed class UnavailableSource : ITelemetrySource
{
    public string Status => "No telemetry: AMS2 shared memory is only available on Windows";

    public Ams2Frame? TryRead() => null;

    public void Dispose() { }
}
