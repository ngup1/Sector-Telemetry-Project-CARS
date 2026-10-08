using System.Diagnostics;
using System.Text.Json;
using SectorTelemetry.Ams2;
using SectorTelemetry.Sources;

namespace SectorTelemetry.Services;

/// <summary>
/// Polls the telemetry source as fast as the timer allows (so gaps and sector splits are precise),
/// and pushes a dashboard snapshot to clients at <see cref="BroadcastInterval"/>.
/// </summary>
public sealed class TelemetryService(
    ITelemetrySource source,
    TrackMapBuilder map,
    TimingTracker timing,
    DashboardHub hub,
    ILogger<TelemetryService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(8);
    private static readonly TimeSpan BroadcastInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(2);

    private readonly object _lock = new();
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private Ams2Frame? _latest;
    private TimeSpan _lastFrameAt;
    private TimeSpan _lastBroadcast;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Telemetry tick failed");
            }
        }
    }

    private void Tick()
    {
        var now = _watch.Elapsed;
        var frame = source.TryRead();
        if (frame != null)
        {
            lock (_lock)
            {
                double dt = _latest == null ? 0 : Math.Min((now - _lastFrameAt).TotalSeconds, 0.25);
                map.Update(frame);
                timing.Update(frame, dt);
                _latest = frame;
                _lastFrameAt = now;
            }
        }

        if (now - _lastBroadcast < BroadcastInterval || hub.ClientCount == 0) return;
        _lastBroadcast = now;

        object payload;
        lock (_lock)
        {
            bool stale = _latest == null || now - _lastFrameAt > StaleAfter;
            bool inSession = _latest?.GameState is GameState.InGamePlaying or GameState.InGamePaused
                or GameState.InGameInMenuTimeTicking or GameState.InGameReplay;
            if (_latest != null && inSession && !stale)
                payload = DashboardSnapshot.Build(_latest, timing, map, source.Status);
            else if (_latest != null && !stale)
                payload = DashboardSnapshot.StatusOnly($"{source.Status} - in menus");
            else
                payload = DashboardSnapshot.StatusOnly(source.Status);
        }
        hub.Broadcast(JsonSerializer.SerializeToUtf8Bytes(payload));
    }

    public object TrackSnapshot()
    {
        lock (_lock) return map.Snapshot();
    }

    public object? CarLaps(int index)
    {
        if (index < 0 || index >= Ams2Constants.StoredParticipantsMax) return null;
        lock (_lock) return DashboardSnapshot.CarLaps(timing, index);
    }
}
