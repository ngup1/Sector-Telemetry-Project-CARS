using SectorTelemetry.Ams2;

namespace SectorTelemetry.Services;

public sealed class LapRecord
{
    public int Lap { get; init; }
    public float Time { get; set; }
    public float[] Sectors { get; init; } = [-1, -1, -1];
    public bool Invalid { get; init; }
    public bool Pitted { get; init; }
    public float? FuelUsed { get; set; }
}

/// <summary>Timing state the game does not provide directly, derived frame by frame for one car.</summary>
public sealed class CarTiming
{
    private const double HistorySpacing = 5; // metres
    private const int HistoryMax = 6000; // ~30 km

    public string Name = "";
    public double TotalDistance;
    public int LapsCompleted = -1;
    public int Sector = -1;
    public float[] Cur = [-1, -1, -1];
    public float[] Prev = [-1, -1, -1];
    public float[] Best = [-1, -1, -1];
    public float LastLap = -1;
    public float BestLap = -1;
    public bool CurInvalid;
    public double LapStartClock, SectorStartClock;
    public float GameLastLapSeen = -1;
    public PitMode PitMode;
    public bool PittedThisLap;
    public int PitStops;
    public double PitLaneEnterClock = -1;
    public float LastPitLaneTime = -1;
    public readonly List<LapRecord> Laps = [];

    // (total distance, session clock) samples used to compute time gaps between cars.
    private readonly List<(double D, double T)> _history = [];

    private double _nowD, _nowT;

    public void AddHistory(double d, double clock)
    {
        _nowD = d;
        _nowT = clock;
        if (_history.Count > 0)
        {
            double last = _history[^1].D;
            if (d < last - 100) _history.Clear(); // went backwards: restart/teleport
            else if (d < last + HistorySpacing) return;
        }
        _history.Add((d, clock));
        if (_history.Count > HistoryMax) _history.RemoveRange(0, _history.Count - HistoryMax);
    }

    /// <summary>Session clock when this car was at total distance d, or null if outside the recorded history.</summary>
    public double? TimeAt(double d)
    {
        if (_history.Count < 2 || d < _history[0].D || d > _nowD) return null;
        if (d > _history[^1].D)
        {
            // Between the last stored sample and where the car is right now.
            var (dl, tl) = _history[^1];
            return _nowD <= dl ? tl : tl + (_nowT - tl) * (d - dl) / (_nowD - dl);
        }
        int lo = 0, hi = _history.Count - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (_history[mid].D < d) lo = mid; else hi = mid;
        }
        var (d0, t0) = _history[lo];
        var (d1, t1) = _history[hi];
        return d1 <= d0 ? t0 : t0 + (t1 - t0) * (d - d0) / (d1 - d0);
    }
}

/// <summary>The viewed car's lap time at every <see cref="Spacing"/> metres, for the live delta.</summary>
public sealed class LapTrace
{
    public const float Spacing = 10f;

    public float LapTime = -1;
    public int Lap;
    public readonly float[] Time;

    public LapTrace(float trackLength)
    {
        Time = Enumerable.Repeat(-1f, (int)Math.Ceiling(trackLength / Spacing) + 1).ToArray();
    }

    public double Coverage => Time.Count(t => t >= 0) / (double)Time.Length;

    /// <summary>True if recording began at the start line, i.e. this is a whole lap rather than one joined mid-way.</summary>
    public bool StartedAtLine => Time[0] >= 0 || Time[1] >= 0;

    public void Record(float lapDist, float time)
    {
        int i = (int)(lapDist / Spacing);
        if (i >= 0 && i < Time.Length && Time[i] < 0) Time[i] = time;
    }

    public float? TimeAt(float lapDist)
    {
        float idx = lapDist / Spacing;
        int i0 = (int)idx;
        if (i0 < 0 || i0 + 1 >= Time.Length) return null;
        float a = Time[i0], b = Time[i0 + 1];
        if (a < 0 || b < 0) return a >= 0 ? a : null;
        return a + (b - a) * (idx - i0);
    }
}

/// <summary>
/// Derives F1-style timing from successive frames: per-car sector splits, lap history, pit stops,
/// time gaps (by comparing when cars passed the same point), session-best tracking, live delta and fuel use.
/// </summary>
public sealed class TimingTracker(TrackMapBuilder map)
{
    private const float SameTimeEpsilon = 0.0005f;

    private readonly CarTiming[] _cars = Enumerable.Range(0, Ams2Constants.StoredParticipantsMax).Select(_ => new CarTiming()).ToArray();
    private string _sessionKey = "";
    private bool? _sectorsOneBased;
    private float _trackLength;

    // Viewed-car state
    private int _playerIndex = -1;
    private LapTrace? _currentTrace;
    private float _prevCurrentTime;
    private bool _prevLapInvalid;
    private float _fuelAtLapStart = -1;
    private readonly List<float> _fuelPerLap = [];

    public double Clock { get; private set; }
    /// <summary>Best valid full lap of the viewed car.</summary>
    public LapTrace? BestTrace { get; private set; }
    /// <summary>Most recent full lap of the viewed car (valid or not).</summary>
    public LapTrace? LastTrace { get; private set; }
    public float[] SessionBestSectors { get; } = [-1, -1, -1];
    public float SessionBestLap { get; private set; } = -1;
    public int SessionBestLapIndex { get; private set; } = -1;
    public float? DeltaToBest { get; private set; }
    public float? DeltaToLast { get; private set; }
    public float? FuelPerLap => _fuelPerLap.Count == 0 ? null : _fuelPerLap.TakeLast(5).Average();

    public CarTiming Car(int i) => _cars[i];

    public void Update(Ams2Frame f, double dt)
    {
        string key = $"{f.TrackLocation}|{f.TrackVariation}|{f.SessionState}";
        int viewed = f.ViewedParticipantIndex;
        bool playerWentBack = viewed >= 0 && viewed == _playerIndex
            && f.Participants[viewed].LapsCompleted < _cars[viewed].LapsCompleted;
        if (key != _sessionKey || f.GameState == GameState.InGameRestarting || playerWentBack)
            Reset(key, f.TrackLength);

        if (f.GameState is GameState.InGamePlaying or GameState.InGameInMenuTimeTicking)
            Clock += dt;

        int n = Math.Min(f.NumParticipants, Ams2Constants.StoredParticipantsMax);
        for (int i = 0; i < n; i++)
            if (f.Participants[i].IsActive) UpdateCar(f, i);

        RecomputeBests(f, n);

        if (viewed >= 0 && viewed < n) UpdatePlayer(f, viewed);
    }

    private void Reset(string key, float trackLength)
    {
        _sessionKey = key;
        _trackLength = trackLength;
        _sectorsOneBased = null;
        for (int i = 0; i < _cars.Length; i++) _cars[i] = new CarTiming();
        Array.Fill(SessionBestSectors, -1);
        SessionBestLap = -1;
        SessionBestLapIndex = -1;
        BestTrace = null;
        LastTrace = null;
        _currentTrace = null;
        _playerIndex = -1;
        _fuelAtLapStart = -1;
        _fuelPerLap.Clear();
        DeltaToBest = null;
        DeltaToLast = null;
        Clock = 0;
    }

    private int NormalizeSector(int raw)
    {
        // The header documents mCurrentSector as starting at 0, but some PCars2-derived games report 1..3.
        if (raw == 0) _sectorsOneBased ??= false;
        if (raw == 3) _sectorsOneBased ??= true;
        int s = _sectorsOneBased == true ? raw - 1 : raw;
        return s is >= 0 and <= 2 ? s : -1;
    }

    private void UpdateCar(Ams2Frame f, int i)
    {
        var p = f.Participants[i];
        var c = _cars[i];
        if (c.Name != p.Name)
        {
            c = _cars[i] = new CarTiming { Name = p.Name };
        }

        int laps = (int)p.LapsCompleted;
        int sector = NormalizeSector(p.CurrentSector);
        float lapDist = Math.Clamp(p.CurrentLapDistance, 0, Math.Max(_trackLength, 1));
        c.TotalDistance = laps * (double)_trackLength + lapDist;
        c.AddHistory(c.TotalDistance, Clock);

        if (c.LapsCompleted < 0)
        {
            // First sight of this car: we can't time the lap in progress.
            c.LapsCompleted = laps;
            c.Sector = sector;
            c.LapStartClock = c.SectorStartClock = double.NaN;
            c.GameLastLapSeen = f.LastLapTimes[i];
            c.PitMode = f.PitModes[i];
            return;
        }

        if (laps > c.LapsCompleted)
        {
            CompleteLap(f, i, c, laps);
        }
        else if (sector > c.Sector && c.Sector >= 0 && sector - c.Sector == 1)
        {
            c.Cur[c.Sector] = SectorTime(f.CurrentSectorTimes[c.Sector][i], c.SectorStartClock);
            map.RecordSectorBoundary(c.Sector, lapDist);
            c.SectorStartClock = Clock;
        }
        if (sector >= 0) c.Sector = sector;

        // The game's last-lap time can land a frame or two after the line crossing; prefer it once it does.
        float gameLast = f.LastLapTimes[i];
        if (gameLast > 0 && gameLast != c.GameLastLapSeen)
        {
            c.GameLastLapSeen = gameLast;
            c.LastLap = gameLast;
            if (c.Prev[0] > 0 && c.Prev[1] > 0 && gameLast - c.Prev[0] - c.Prev[1] > 0)
                c.Prev[2] = gameLast - c.Prev[0] - c.Prev[1];
            if (c.Laps.Count > 0 && c.Laps[^1].Lap == c.LapsCompleted)
            {
                c.Laps[^1].Time = gameLast;
                c.Laps[^1].Sectors[2] = c.Prev[2];
            }
        }

        c.CurInvalid |= f.LapsInvalidated[i];
        UpdatePit(f.PitModes[i], c);
    }

    private float SectorTime(float gameValue, double startClock)
    {
        // Our clock-based split is ±1 frame; use the game's exact value when it agrees with it.
        float measured = double.IsNaN(startClock) ? -1 : (float)(Clock - startClock);
        if (gameValue > 0 && (measured < 0 || Math.Abs(gameValue - measured) < 0.5f)) return gameValue;
        return measured;
    }

    private void CompleteLap(Ams2Frame f, int i, CarTiming c, int laps)
    {
        if (c.Sector == 2)
            c.Cur[2] = SectorTime(f.CurrentSectorTimes[2][i], c.SectorStartClock);

        bool timed = !double.IsNaN(c.LapStartClock);
        float lapTime = timed ? (float)(Clock - c.LapStartClock) : -1;
        float gameLast = f.LastLapTimes[i];
        if (gameLast > 0 && gameLast != c.GameLastLapSeen)
        {
            lapTime = gameLast;
            c.GameLastLapSeen = gameLast;
        }
        if (lapTime > 0 && c.Cur[0] > 0 && c.Cur[1] > 0 && lapTime - c.Cur[0] - c.Cur[1] > 0)
            c.Cur[2] = lapTime - c.Cur[0] - c.Cur[1];

        if (lapTime > 0)
        {
            c.LastLap = lapTime;
            c.Laps.Add(new LapRecord
            {
                Lap = laps,
                Time = lapTime,
                Sectors = (float[])c.Cur.Clone(),
                Invalid = c.CurInvalid,
                Pitted = c.PittedThisLap,
            });
            if (c.Laps.Count > 200) c.Laps.RemoveAt(0);
        }

        if (!c.CurInvalid)
        {
            for (int s = 0; s < 3; s++)
                if (c.Cur[s] > 0 && (c.Best[s] < 0 || c.Cur[s] < c.Best[s])) c.Best[s] = c.Cur[s];
            if (lapTime > 0 && (c.BestLap < 0 || lapTime < c.BestLap)) c.BestLap = lapTime;
        }

        c.Prev = c.Cur;
        c.Cur = [-1, -1, -1];
        c.CurInvalid = false;
        c.PittedThisLap = c.PitMode != PitMode.None;
        c.LapsCompleted = laps;
        c.LapStartClock = c.SectorStartClock = Clock;
    }

    private void UpdatePit(PitMode mode, CarTiming c)
    {
        bool wasOnTrack = c.PitMode == PitMode.None;
        bool onTrack = mode == PitMode.None;
        if (wasOnTrack && mode == PitMode.DrivingIntoPits) c.PitLaneEnterClock = Clock;
        if (mode == PitMode.InPit && c.PitMode != PitMode.InPit) c.PitStops++;
        if (!onTrack) c.PittedThisLap = true;
        if (!wasOnTrack && onTrack && c.PitLaneEnterClock >= 0)
        {
            c.LastPitLaneTime = (float)(Clock - c.PitLaneEnterClock);
            c.PitLaneEnterClock = -1;
        }
        c.PitMode = mode;
    }

    private void RecomputeBests(Ams2Frame f, int n)
    {
        Array.Fill(SessionBestSectors, -1);
        SessionBestLap = -1;
        SessionBestLapIndex = -1;
        for (int i = 0; i < n; i++)
        {
            if (!f.Participants[i].IsActive) continue;
            var c = _cars[i];
            // Merge in the game's own fastest times, which include laps from before we started watching.
            for (int s = 0; s < 3; s++)
            {
                float g = f.FastestSectorTimes[s][i];
                if (g > 0 && (c.Best[s] < 0 || g < c.Best[s])) c.Best[s] = g;
                if (c.Best[s] > 0 && (SessionBestSectors[s] < 0 || c.Best[s] < SessionBestSectors[s])) SessionBestSectors[s] = c.Best[s];
            }
            float gl = f.FastestLapTimes[i];
            if (gl > 0 && (c.BestLap < 0 || gl < c.BestLap)) c.BestLap = gl;
            if (c.BestLap > 0 && (SessionBestLap < 0 || c.BestLap < SessionBestLap))
            {
                SessionBestLap = c.BestLap;
                SessionBestLapIndex = i;
            }
        }
    }

    /// <summary>"purple" = session best, "green" = personal best, "yellow" = neither.</summary>
    public string SectorColour(int car, int sector, float time)
    {
        if (time <= 0) return "";
        if (SessionBestSectors[sector] > 0 && time <= SessionBestSectors[sector] + SameTimeEpsilon) return "purple";
        var best = _cars[car].Best[sector];
        if (best > 0 && time <= best + SameTimeEpsilon) return "green";
        return "yellow";
    }

    public string LapColour(int car, float time)
    {
        if (time <= 0) return "";
        if (SessionBestLap > 0 && time <= SessionBestLap + SameTimeEpsilon) return "purple";
        var best = _cars[car].BestLap;
        if (best > 0 && time <= best + SameTimeEpsilon) return "green";
        return "yellow";
    }

    /// <summary>Seconds that <paramref name="behind"/> trails <paramref name="ahead"/>, or whole laps if lapped.</summary>
    public (double? Seconds, int Laps) Gap(int ahead, int behind)
    {
        var a = _cars[ahead];
        var b = _cars[behind];
        double diff = a.TotalDistance - b.TotalDistance;
        if (_trackLength > 0 && diff >= _trackLength)
        {
            // Lapped only if the car ahead passed this exact point a full lap or more ago.
            int laps = (int)(diff / _trackLength);
            return (null, laps);
        }
        var t = a.TimeAt(b.TotalDistance);
        return (t == null ? null : Math.Max(0, Clock - t.Value), 0);
    }

    private void UpdatePlayer(Ams2Frame f, int viewed)
    {
        var p = f.Participants[viewed];
        float lapDist = p.CurrentLapDistance;
        float fuelLitres = f.FuelLevel * f.FuelCapacity;

        if (viewed != _playerIndex)
        {
            _playerIndex = viewed;
            _currentTrace = null;
            BestTrace = null;
        LastTrace = null;
            _fuelAtLapStart = -1;
            _fuelPerLap.Clear();
        }

        var c = _cars[viewed];
        bool newLap = _currentTrace != null && (int)p.LapsCompleted > _currentTrace.Lap;
        if (newLap)
        {
            var done = _currentTrace!;
            // The game's lap timer just before the line is the most precise lap time we have.
            done.LapTime = _prevCurrentTime;
            bool fullLap = done.StartedAtLine && done.Coverage > 0.9;
            LastTrace = fullLap ? done : null;
            if (fullLap && !_prevLapInvalid && (BestTrace == null || done.LapTime < BestTrace.LapTime))
                BestTrace = done;

            if (_fuelAtLapStart > 0 && !(c.Laps.Count > 0 && c.Laps[^1].Pitted) && _fuelAtLapStart - fuelLitres > 0.01f)
            {
                float used = _fuelAtLapStart - fuelLitres;
                _fuelPerLap.Add(used);
                if (c.Laps.Count > 0 && c.Laps[^1].Lap == (int)p.LapsCompleted) c.Laps[^1].FuelUsed = used;
            }
        }

        if (_currentTrace == null || newLap)
        {
            _currentTrace = new LapTrace(f.TrackLength) { Lap = (int)p.LapsCompleted };
            _fuelAtLapStart = fuelLitres;
        }
        if (f.PitMode == PitMode.InPit) _fuelAtLapStart = -1; // refuelling makes this lap's usage meaningless

        _currentTrace.Record(lapDist, f.CurrentTime);

        DeltaToBest = Delta(BestTrace, lapDist, f.CurrentTime);
        DeltaToLast = Delta(LastTrace, lapDist, f.CurrentTime);

        _prevCurrentTime = f.CurrentTime;
        _prevLapInvalid = f.LapInvalidated;
    }

    private static float? Delta(LapTrace? reference, float lapDist, float currentTime)
    {
        var refTime = reference?.TimeAt(lapDist);
        return refTime != null && currentTime > 0 ? currentTime - refTime : null;
    }
}
