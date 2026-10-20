using System.Diagnostics;
using System.Globalization;
using System.Text;
using SectorTelemetry.Ams2;

namespace SectorTelemetry.Services;

/// <summary>
/// Writes the viewed car's telemetry to a CSV file, one row per game frame, while recording is on.
/// Units match the dashboard's wire format: km/h, °C, seconds, metres. Files go to
/// <see cref="Dir"/> and are named after the start time, track and car.
/// </summary>
public sealed class SessionRecorder(ILogger<SessionRecorder> logger) : IDisposable
{
    public static readonly string Dir = Path.Combine(AppPaths.DataDir, "recordings");

    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);
    private static readonly string[] Wheels = ["FL", "FR", "RL", "RR"];
    private static readonly string[] Columns =
    [
        "time", "clock", "lap", "lapDist", "lapTime", "lastLap", "sector", "invalid", "inPit",
        "speed", "rpm", "gear", "throttle", "brake", "clutch", "steering", "handbrake",
        "latG", "longG", "vertG", "x", "y", "z", "fuel", "brakeBias", "drs", "delta",
        .. PerWheel("tyreTemp"), .. PerWheel("pressure"), .. PerWheel("brakeTemp"),
        .. PerWheel("susp"), .. PerWheel("rideHeight"),
    ];

    private readonly object _lock = new();
    private readonly Stopwatch _watch = new();
    private readonly StringBuilder _row = new(512);
    private bool _active;
    private StreamWriter? _writer;
    private string? _file;
    private long _rows;
    private TimeSpan _lastFlush;

    private static IEnumerable<string> PerWheel(string name) => Wheels.Select(w => name + w);

    /// <summary>
    /// Starts recording. The file is created on the first in-session frame, so its name can include the track and car.
    /// </summary>
    public void Start()
    {
        lock (_lock)
        {
            if (_active) return;
            _active = true;
            _file = null;
            _rows = 0;
            _watch.Reset();
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _active = false;
            Close();
        }
    }

    public object Info()
    {
        lock (_lock)
        {
            return new
            {
                active = _active,
                file = _file,
                rows = _rows,
                seconds = Math.Round(_watch.Elapsed.TotalSeconds, 1),
            };
        }
    }

    /// <summary>Appends one row for the viewed car. Called by the poll loop for every new frame.</summary>
    public void Record(Ams2Frame f, TimingTracker timing)
    {
        lock (_lock)
        {
            if (!_active) return;
            if (f.GameState is not (GameState.InGamePlaying or GameState.InGameInMenuTimeTicking)) return;
            int i = f.ViewedParticipantIndex;
            if (i < 0 || i >= Math.Min(f.NumParticipants, Ams2Constants.StoredParticipantsMax)) return;

            try
            {
                if (_writer == null) Open(f, i);
                WriteRow(f, timing, i);
                if (_watch.Elapsed - _lastFlush > FlushInterval)
                {
                    _writer!.Flush();
                    _lastFlush = _watch.Elapsed;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Recording failed; stopping");
                _active = false;
                Close();
            }
        }
    }

    public static IEnumerable<object> List()
    {
        if (!Directory.Exists(Dir)) return [];
        return new DirectoryInfo(Dir).GetFiles("*.csv")
            .OrderByDescending(fi => fi.LastWriteTimeUtc)
            .Select(fi => new { name = fi.Name, size = fi.Length, modified = fi.LastWriteTimeUtc });
    }

    /// <summary>Full path of a recording, or null if the name isn't a plain file name of an existing CSV.</summary>
    public static string? PathOf(string name)
    {
        if (name != Path.GetFileName(name) || !name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) return null;
        var path = Path.Combine(Dir, name);
        return File.Exists(path) ? path : null;
    }

    private void Open(Ams2Frame f, int i)
    {
        Directory.CreateDirectory(Dir);
        string track = string.IsNullOrEmpty(f.TranslatedTrackLocation) ? f.TrackLocation : f.TranslatedTrackLocation;
        string name = $"{DateTime.Now:yyyy-MM-dd_HHmmss}_{Safe(track)}_{Safe(f.TrackVariation)}_{Safe(f.CarName)}.csv";
        string path = Path.Combine(Dir, name);
        _writer = new StreamWriter(path, false, new UTF8Encoding(false));
        _writer.WriteLine(string.Join(',', Columns));
        _file = name;
        _watch.Restart();
        _lastFlush = TimeSpan.Zero;
        logger.LogInformation("Recording {Car} to {Path}", f.Participants[i].Name, path);
    }

    private void Close()
    {
        try
        {
            _writer?.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not close recording {File}", _file);
        }
        _writer = null;
        _watch.Stop();
    }

    private void WriteRow(Ams2Frame f, TimingTracker timing, int i)
    {
        var p = f.Participants[i];
        var c = timing.Car(i);
        const double g = 9.80665;

        _row.Clear();
        N(_watch.Elapsed.TotalSeconds, 3);
        N(timing.Clock, 3);
        I(p.CurrentLap);
        N(p.CurrentLapDistance, 1);
        N(f.CurrentTime > 0 ? f.CurrentTime : double.NaN, 3);
        N(f.LastLapTime > 0 ? f.LastLapTime : double.NaN, 3);
        I(c.Sector >= 0 ? c.Sector + 1 : null);
        I(f.LapInvalidated ? 1 : 0);
        I(f.PitMode == PitMode.None ? 0 : 1);
        N(f.Speed * 3.6, 2);
        N(f.Rpm, 0);
        I(f.Gear);
        N(f.Throttle, 3);
        N(f.Brake, 3);
        N(f.Clutch, 3);
        N(f.Steering, 3);
        N(f.HandBrake, 3);
        N(f.LocalAcceleration[0] / g, 3);
        N(f.LocalAcceleration[2] / g, 3);
        N(f.LocalAcceleration[1] / g, 3);
        N(p.WorldPosition[0], 2);
        N(p.WorldPosition[1], 2);
        N(p.WorldPosition[2], 2);
        N(f.FuelLevel * f.FuelCapacity, 3);
        N(f.BrakeBias, 3);
        I(f.DrsState.HasFlag(DrsState.Active) ? 1 : 0);
        N(timing.DeltaToBest ?? double.NaN, 3);
        for (int w = 0; w < Ams2Constants.TyreMax; w++) N(f.TyreTemp[w], 1);
        for (int w = 0; w < Ams2Constants.TyreMax; w++) N(Psi(f.AirPressure[w]), 2);
        for (int w = 0; w < Ams2Constants.TyreMax; w++) N(f.BrakeTempCelsius[w], 0);
        for (int w = 0; w < Ams2Constants.TyreMax; w++) N(f.SuspensionTravel[w] * 1000, 1);
        for (int w = 0; w < Ams2Constants.TyreMax; w++) N(f.RideHeight[w], 2);

        _row.Length--; // trailing comma
        _writer!.WriteLine(_row);
        _rows++;
    }

    /// <summary>Appends a number (empty when NaN/infinite) and a comma.</summary>
    private void N(double v, int digits)
    {
        if (double.IsFinite(v)) _row.Append(Math.Round(v, digits).ToString(CultureInfo.InvariantCulture));
        _row.Append(',');
    }

    private void I(long? v)
    {
        if (v is { } x) _row.Append(x);
        _row.Append(',');
    }

    // Same conversion as the dashboard: values in the kPa range are converted to PSI.
    private static double Psi(float v) => v > 60 ? v * 0.1450377 : v;

    private static string Safe(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(s.Select(ch => invalid.Contains(ch) || ch is ' ' or ',' ? '-' : ch).ToArray()).Trim('-');
        return cleaned.Length == 0 ? "unknown" : cleaned[..Math.Min(cleaned.Length, 40)];
    }

    public void Dispose() => Stop();
}
