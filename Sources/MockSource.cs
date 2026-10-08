using System.Diagnostics;
using SectorTelemetry.Ams2;

namespace SectorTelemetry.Sources;

/// <summary>
/// Simulated 20-car race on a procedurally generated circuit, producing frames shaped like the real shared memory.
/// Lets the dashboard be developed without the game (e.g. on macOS). Enable with --mock.
/// </summary>
public sealed class MockSource : ITelemetrySource
{
    private const int CarCount = 20;
    private const int PlayerIndex = 6;
    private const int RaceLaps = 25;
    private const double Ds = 2.0; // track sample spacing, metres
    private const double G = 9.81;
    private const double PitLaneSpeed = 80 / 3.6;
    private const double FuelPerLap = 1.6;
    private const float FuelCapacity = 110f;

    private static readonly string[] Drivers =
    [
        "A. Moreau", "K. Lindqvist", "T. Okafor", "R. Castellano", "J. Whitfield", "M. Sato", "You",
        "L. Brandt", "D. Novak", "S. Haddad", "P. Oliveira", "E. Kowalski", "N. Fischer", "H. Tanaka",
        "C. Duval", "F. Romano", "B. Jensen", "V. Ivanova", "G. Mendes", "O. Larsen",
    ];

    private static readonly double[] GearTop = [24, 33.5, 43, 52.5, 62, 71.5, 81, 91]; // m/s at max rpm

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Random _rng = new();
    private readonly double[] _x, _y, _k, _vProfile;
    private readonly double _length;

    private Car[] _cars = [];
    private double _raceClock;
    private double _lastTick;
    private double _finishedAt = -1;
    private readonly PlayerState _player = new();

    public string Status => "Mock data";

    public MockSource()
    {
        (_x, _y, _length) = BuildTrack();
        _k = Curvature(_x, _y);
        _vProfile = SpeedProfile(_k);
        ResetRace();
    }

    public Ams2Frame? TryRead()
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - _lastTick, 0, 0.1);
        _lastTick = now;
        Step(dt);
        return BuildFrame();
    }

    public void Dispose() { }

    // ---------------------------------------------------------------- track

    private static (double[] x, double[] y, double length) BuildTrack()
    {
        const int n = 6000;
        var px = new double[n + 1];
        var py = new double[n + 1];
        for (int i = 0; i <= n; i++)
        {
            double t = 2 * Math.PI * i / n;
            double r = 1 + 0.30 * Math.Sin(2 * t) + 0.12 * Math.Sin(3 * t + 0.8) + 0.07 * Math.Sin(5 * t + 2.1)
                         + 0.05 * Math.Sin(9 * t + 1.0) + 0.03 * Math.Sin(7 * t);
            px[i] = r * Math.Cos(t);
            py[i] = r * Math.Sin(t);
        }

        var cum = new double[n + 1];
        for (int i = 1; i <= n; i++) cum[i] = cum[i - 1] + double.Hypot(px[i] - px[i - 1], py[i] - py[i - 1]);
        const double targetLength = 4600;
        double scale = targetLength / cum[n];

        int m = (int)(targetLength / Ds);
        var x = new double[m];
        var y = new double[m];
        int j = 0;
        for (int i = 0; i < m; i++)
        {
            double d = i * Ds / scale;
            while (j < n - 1 && cum[j + 1] < d) j++;
            double f = (d - cum[j]) / (cum[j + 1] - cum[j]);
            x[i] = (px[j] + f * (px[j + 1] - px[j])) * scale;
            y[i] = (py[j] + f * (py[j + 1] - py[j])) * scale;
        }
        return (x, y, m * Ds);
    }

    private static double[] Curvature(double[] x, double[] y)
    {
        int m = x.Length;
        const int w = 6;
        var k = new double[m];
        for (int i = 0; i < m; i++)
        {
            int a = (i - w + m) % m, c = (i + w) % m;
            double abx = x[i] - x[a], aby = y[i] - y[a];
            double bcx = x[c] - x[i], bcy = y[c] - y[i];
            double acx = x[c] - x[a], acy = y[c] - y[a];
            double cross = abx * acy - aby * acx;
            k[i] = 2 * cross / (double.Hypot(abx, aby) * double.Hypot(bcx, bcy) * double.Hypot(acx, acy));
        }
        return k;
    }

    private static double[] SpeedProfile(double[] k)
    {
        int m = k.Length;
        var v = new double[m];
        for (int i = 0; i < m; i++) v[i] = Math.Min(90, Math.Sqrt(3.6 * G / Math.Max(Math.Abs(k[i]), 1e-6)));

        // Two laps each way so the wrap-around settles.
        for (int pass = 0; pass < 2 * m; pass++)
        {
            int i = pass % m, prev = (i - 1 + m) % m;
            double accel = 13 * (1 - v[prev] / 96) + 1;
            v[i] = Math.Min(v[i], Math.Sqrt(v[prev] * v[prev] + 2 * accel * Ds));
        }
        for (int pass = 2 * m - 1; pass >= 0; pass--)
        {
            int i = pass % m, next = (i + 1) % m;
            v[i] = Math.Min(v[i], Math.Sqrt(v[next] * v[next] + 2 * 38 * Ds));
        }
        return v;
    }

    private double Sample(double[] arr, double lapDist)
    {
        double idx = ((lapDist % _length) + _length) % _length / Ds;
        int i0 = (int)idx % arr.Length, i1 = (i0 + 1) % arr.Length;
        double f = idx - Math.Floor(idx);
        return arr[i0] + f * (arr[i1] - arr[i0]);
    }

    // ---------------------------------------------------------------- simulation

    private sealed class Car
    {
        public string Name = "";
        public double Pace;
        public double LapNoise = 1;
        public double Dist;
        public int LapsCompleted;
        public int Sector;
        public double LapStart, SectorStart;
        public float[] CurSectors = [-1, -1, -1];
        public float[] BestSectors = [-1, -1, -1];
        public float LastLap = -1, BestLap = -1;
        public bool Invalid;
        public double Speed;
        public PitMode Pit;
        public double PitTimer;
        public int PitLap;
        public bool Finished;
        public double FinishOrderKey;
        public double LapDist(double length) => Dist - LapsCompleted * length;
    }

    private sealed class PlayerState
    {
        public double Fuel = 85;
        public double[] Wear = new double[4];
        public double[] BrakeTemp = [350, 350, 330, 330];
        public double[] TyreTemp = [85, 85, 85, 85];
        public double PrevSpeed;
        public double Odometer = 1240;
    }

    private void ResetRace()
    {
        _raceClock = 0;
        _finishedAt = -1;
        _player.Fuel = 85;
        Array.Clear(_player.Wear);

        var paces = Enumerable.Range(0, CarCount).Select(i => 0.965 + 0.035 * _rng.NextDouble()).ToArray();
        // Grid roughly by pace, with some shuffling.
        var gridOrder = Enumerable.Range(0, CarCount).OrderByDescending(i => paces[i] + _rng.NextDouble() * 0.01).ToArray();

        _cars = new Car[CarCount];
        for (int i = 0; i < CarCount; i++)
        {
            int slot = Array.IndexOf(gridOrder, i);
            _cars[i] = new Car
            {
                Name = Drivers[i],
                Pace = paces[i],
                Dist = (CarCount - slot) * 8.0,
                PitLap = 8 + _rng.Next(8),
            };
        }
    }

    private void Step(double dt)
    {
        _raceClock += dt;
        if (_finishedAt > 0 && _raceClock - _finishedAt > 15)
        {
            ResetRace();
            return;
        }

        foreach (var car in _cars)
        {
            if (car.Finished)
            {
                car.Speed = Math.Max(0, car.Speed - 8 * dt);
                car.Dist += car.Speed * dt;
                continue;
            }

            double lapDist = car.LapDist(_length);
            car.Speed = Sample(_vProfile, lapDist) * car.Pace * car.LapNoise;

            switch (car.Pit)
            {
                case PitMode.InPit:
                    car.Speed = 0;
                    car.PitTimer -= dt;
                    if (car.PitTimer <= 0) car.Pit = PitMode.DrivingOutOfPits;
                    break;
                case PitMode.DrivingIntoPits:
                case PitMode.DrivingOutOfPits:
                    car.Speed = Math.Min(car.Speed, PitLaneSpeed);
                    if (car.Pit == PitMode.DrivingOutOfPits && lapDist > 350) car.Pit = PitMode.None;
                    break;
                default:
                    if (car.LapsCompleted == car.PitLap - 1 && lapDist > _length - 450) car.Pit = PitMode.DrivingIntoPits;
                    break;
            }

            car.Dist += car.Speed * dt;
            lapDist = car.LapDist(_length);

            if (car.Sector < 2 && lapDist >= _length * (car.Sector + 1) / 3)
            {
                CompleteSector(car, car.Sector);
                car.Sector++;
            }

            if (lapDist >= _length)
                CompleteLap(car);
        }

        if (_finishedAt < 0 && _cars.All(c => c.Finished))
            _finishedAt = _raceClock;

        StepPlayer(dt);
    }

    private void CompleteSector(Car car, int sector)
    {
        float t = (float)(_raceClock - car.SectorStart);
        car.SectorStart = _raceClock;
        // Sector 1 of the opening lap starts from the grid, so it isn't a comparable time.
        if (car.LapsCompleted == 0 && sector == 0) return;
        car.CurSectors[sector] = t;
        if (car.LapsCompleted > 0 && (car.BestSectors[sector] < 0 || t < car.BestSectors[sector]))
            car.BestSectors[sector] = t;
    }

    private void CompleteLap(Car car)
    {
        CompleteSector(car, 2);
        float lap = (float)(_raceClock - car.LapStart);
        // The first lap starts from the grid, so it isn't a full lap.
        if (car.LapsCompleted > 0)
        {
            car.LastLap = lap;
            if (!car.Invalid && (car.BestLap < 0 || lap < car.BestLap)) car.BestLap = lap;
        }

        car.LapsCompleted++;
        car.Sector = 0;
        car.LapStart = _raceClock;
        car.SectorStart = _raceClock;
        car.CurSectors = [-1, -1, -1];
        car.Invalid = _rng.NextDouble() < 0.03;
        car.LapNoise = 1 + (_rng.NextDouble() - 0.5) * 0.008;

        if (car.Pit == PitMode.DrivingIntoPits)
        {
            car.Pit = PitMode.InPit;
            car.PitTimer = 2.4 + _rng.NextDouble();
            if (car == _cars[PlayerIndex])
            {
                _player.Fuel = Math.Min(FuelCapacity, _player.Fuel + 30);
                Array.Clear(_player.Wear);
            }
        }

        bool leaderDone = _cars.Any(c => c.Finished) || car.LapsCompleted >= RaceLaps;
        if (leaderDone)
        {
            car.Finished = true;
            car.FinishOrderKey = _raceClock;
        }
    }

    private void StepPlayer(double dt)
    {
        var car = _cars[PlayerIndex];
        double ds = car.Speed * dt;
        _player.Fuel = Math.Max(0, _player.Fuel - ds / _length * FuelPerLap);
        _player.Odometer += ds / 1000;

        double lapDist = car.LapDist(_length);
        double k = Sample(_k, lapDist);
        double lon = dt > 0 ? (car.Speed - _player.PrevSpeed) / dt : 0;
        _player.PrevSpeed = car.Speed;
        double lat = car.Speed * car.Speed * k;
        double brake = lon < -3 ? Math.Clamp(-lon / 38, 0, 1) : 0;

        for (int w = 0; w < 4; w++)
        {
            bool front = w < 2;
            bool outside = (w % 2 == 0) == (k < 0);
            double load = Math.Abs(lat) / G * (outside ? 1.3 : 0.7) + (front ? brake * 1.5 : 0);
            double target = 82 + 9 * load + (front ? 0 : 3);
            _player.TyreTemp[w] += (target - _player.TyreTemp[w]) * Math.Min(1, dt * 0.4);
            _player.Wear[w] = Math.Min(1, _player.Wear[w] + ds / _length * (0.004 + 0.002 * load));

            double brakeTarget = 300 + 650 * brake * (front ? 1 : 0.8) + car.Speed * 1.5;
            _player.BrakeTemp[w] += (brakeTarget - _player.BrakeTemp[w]) * Math.Min(1, dt * (brake > 0 ? 2.5 : 0.5));
        }
    }

    // ---------------------------------------------------------------- frame output

    private Ams2Frame BuildFrame()
    {
        var f = new Ams2Frame
        {
            Version = Ams2Constants.SharedMemoryVersion,
            GameState = GameState.InGamePlaying,
            SessionState = SessionState.Race,
            RaceState = RaceState.Racing,
            ViewedParticipantIndex = PlayerIndex,
            NumParticipants = CarCount,
            CarName = "Formula Mock",
            CarClassName = "F-Mock",
            LapsInEvent = RaceLaps,
            TrackLocation = "Mock Ring",
            TrackVariation = "Grand Prix",
            TranslatedTrackLocation = "Mock Ring",
            TranslatedTrackVariation = "Grand Prix",
            TrackLength = (float)_length,
            NumSectors = 3,
            EventTimeRemaining = -1,
            HighestFlagColour = _cars.Any(c => c.Finished) ? FlagColour.Chequered : FlagColour.Green,
            YellowFlagState = YellowFlagState.None,
            AmbientTemperature = 24,
            TrackTemperature = 37,
            WindSpeed = 3.2f,
            WindDirectionX = 0.6f,
            WindDirectionY = 0.8f,
            CloudBrightness = 0.8f,
            EnforcedPitStopLap = -1,
        };

        var order = _cars.Select((c, i) => (c, i))
            .OrderBy(t => t.c.Finished ? 0 : 1)
            .ThenBy(t => t.c.Finished ? t.c.FinishOrderKey : 0)
            .ThenByDescending(t => t.c.Dist)
            .Select(t => t.i)
            .ToArray();

        for (int i = 0; i < CarCount; i++)
        {
            var c = _cars[i];
            double lapDist = Math.Clamp(c.LapDist(_length), 0, _length);
            var p = f.Participants[i];
            p.IsActive = true;
            p.Name = c.Name;
            bool inPitLane = c.Pit is PitMode.DrivingIntoPits or PitMode.InPit or PitMode.DrivingOutOfPits;
            (double x, double z) = PositionAt(lapDist, inPitLane ? 18 : 0);
            p.WorldPosition[0] = (float)x;
            p.WorldPosition[2] = (float)z;
            p.CurrentLapDistance = (float)lapDist;
            p.RacePosition = (uint)(Array.IndexOf(order, i) + 1);
            p.LapsCompleted = (uint)c.LapsCompleted;
            p.CurrentLap = (uint)Math.Min(c.LapsCompleted + 1, RaceLaps);
            p.CurrentSector = c.Sector;

            for (int s = 0; s < 3; s++)
            {
                f.CurrentSectorTimes[s][i] = c.CurSectors[s];
                f.FastestSectorTimes[s][i] = c.BestSectors[s];
            }
            f.FastestLapTimes[i] = c.BestLap;
            f.LastLapTimes[i] = c.LastLap;
            f.LapsInvalidated[i] = c.Invalid;
            f.RaceStates[i] = c.Finished ? RaceState.Finished : RaceState.Racing;
            f.PitModes[i] = c.Pit;
            f.Speeds[i] = (float)c.Speed;
            f.CarNames[i] = "Formula Mock";
            f.CarClassNames[i] = "F-Mock";
            f.HighestFlagColours[i] = f.HighestFlagColour;
            f.Orientations[i][1] = (float)Heading(lapDist);
        }

        FillPlayer(f);
        return f;
    }

    private (double x, double z) PositionAt(double lapDist, double lateralOffset)
    {
        double x = Sample(_x, lapDist), z = Sample(_y, lapDist);
        if (lateralOffset == 0) return (x, z);
        double h = Heading(lapDist);
        return (x - Math.Sin(h) * lateralOffset, z + Math.Cos(h) * lateralOffset);
    }

    private double Heading(double lapDist)
    {
        double dx = Sample(_x, lapDist + Ds) - Sample(_x, lapDist - Ds);
        double dz = Sample(_y, lapDist + Ds) - Sample(_y, lapDist - Ds);
        return Math.Atan2(dz, dx);
    }

    private void FillPlayer(Ams2Frame f)
    {
        var car = _cars[PlayerIndex];
        double lapDist = Math.Clamp(car.LapDist(_length), 0, _length);
        double v = car.Speed;
        double k = Sample(_k, lapDist);
        double vAhead = Sample(_vProfile, lapDist + 15) * car.Pace;
        double lon = (vAhead * vAhead - v * v) / (2 * 15);
        double lat = v * v * k;

        float throttle, brake;
        if (car.Pit == PitMode.InPit) { throttle = 0; brake = 1; }
        else if (lon < -4) { throttle = 0; brake = (float)Math.Clamp(-lon / 38 + 0.15, 0, 1); }
        else { throttle = (float)Math.Clamp(0.35 + lon / 8 + (v > 85 ? 1 : 0), 0, 1); brake = 0; }
        if (car.Pit is PitMode.DrivingIntoPits or PitMode.DrivingOutOfPits) throttle = Math.Min(throttle, 0.3f);

        int gear = Array.FindIndex(GearTop, top => v <= top * 0.96) + 1;
        if (gear == 0) gear = GearTop.Length;
        if (v < 1) gear = car.Pit == PitMode.InPit ? 0 : 1;
        double rpm = gear == 0 ? 4000 : 4500 + 7800 * Math.Clamp(v / GearTop[gear - 1], 0, 1);

        f.Throttle = f.UnfilteredThrottle = throttle;
        f.Brake = f.UnfilteredBrake = brake;
        f.Steering = f.UnfilteredSteering = (float)Math.Clamp(k * 28, -1, 1);
        f.Clutch = f.UnfilteredClutch = 0;
        f.Speed = (float)v;
        f.Rpm = (float)rpm;
        f.MaxRpm = 12500;
        f.Gear = gear;
        f.NumGears = GearTop.Length;
        f.EngineSpeed = (float)(rpm * 2 * Math.PI / 60);
        f.EngineTorque = (float)(throttle * 380);
        f.OdometerKm = (float)_player.Odometer;
        f.CarFlags = CarFlags.EngineActive | (car.Pit is PitMode.DrivingIntoPits or PitMode.DrivingOutOfPits ? CarFlags.SpeedLimiter : 0);
        f.PitMode = car.Pit;
        f.PitSchedule = car.LapsCompleted == car.PitLap - 1 ? PitSchedule.PlayerRequested : PitSchedule.None;

        f.LocalAcceleration[0] = (float)lat;
        f.LocalAcceleration[2] = (float)lon;
        f.LocalVelocity[2] = (float)v;

        f.FuelCapacity = FuelCapacity;
        f.FuelLevel = (float)(_player.Fuel / FuelCapacity);
        f.OilTempCelsius = 104 + throttle * 4;
        f.WaterTempCelsius = 91 + throttle * 3;
        f.OilPressureKPa = (float)(300 + rpm / 40);
        f.WaterPressureKPa = 180;
        f.FuelPressureKPa = 500;

        f.CurrentTime = (float)(_raceClock - car.LapStart);
        f.LastLapTime = car.LastLap;
        f.BestLapTime = car.BestLap;
        f.PersonalFastestLapTime = car.BestLap;
        f.LapInvalidated = car.Invalid;
        for (int s = 0; s < 3; s++)
        {
            f.CurrentSectorTime[s] = car.CurSectors[s];
            f.PersonalFastestSectorTime[s] = car.BestSectors[s];
            f.FastestSectorTime[s] = car.BestSectors[s];
        }
        f.SplitTimeAhead = f.SplitTimeBehind = -1;

        for (int w = 0; w < 4; w++)
        {
            bool front = w < 2;
            double t = _player.TyreTemp[w];
            bool leftSide = w % 2 == 0;
            double camberSpread = front ? 6 : 4;
            f.TyreTemp[w] = (float)t;
            // Left/Right are the tread edges as seen from behind; inner edge runs hotter with negative camber.
            f.TyreTempLeft[w] = (float)(t + (leftSide ? -camberSpread / 2 : camberSpread / 2));
            f.TyreTempCenter[w] = (float)t;
            f.TyreTempRight[w] = (float)(t + (leftSide ? camberSpread / 2 : -camberSpread / 2));
            f.TyreTreadTemp[w] = (float)(t + 273.15);
            f.TyreCarcassTemp[w] = (float)(t - 6 + 273.15);
            f.TyreInternalAirTemp[w] = (float)(t - 12 + 273.15);
            f.TyreRimTemp[w] = (float)(t - 25 + 273.15);
            f.AirPressure[w] = (float)(21.5 + (t - 80) * 0.06);
            f.TyreWear[w] = (float)_player.Wear[w];
            f.BrakeTempCelsius[w] = (float)_player.BrakeTemp[w];
            f.TyreFlags[w] = TyreFlags.Attached | TyreFlags.Inflated | TyreFlags.IsOnGround;
            f.RideHeight[w] = (float)((front ? 3.2 : 5.1) - v / 90 * 0.8 + (front ? brake * 0.6 : 0) * -1);
            f.SuspensionTravel[w] = (float)(0.02 + v / 90 * 0.015);
            f.TyreRps[w] = (float)(v / (2 * Math.PI * 0.33));
            f.TyreCompound[w] = "Medium";
        }

        bool inDrsZone = lapDist > _length - 1100 && lapDist < _length - 150 && Math.Abs(k) < 0.004;
        var ahead = _cars.Where(c => c != car && !c.Finished && c.Dist > car.Dist).OrderBy(c => c.Dist).FirstOrDefault();
        bool withinOneSecond = ahead != null && (ahead.Dist - car.Dist) / Math.Max(v, 1) < 1.0;
        f.DrsState = DrsState.Installed | DrsState.ZoneRules;
        if (withinOneSecond && lapDist > _length - 1300) f.DrsState |= DrsState.AvailableNext;
        if (withinOneSecond && inDrsZone) f.DrsState |= DrsState.AvailableNow | DrsState.Active;

        f.ErsDeploymentMode = ErsDeploymentMode.Balanced;
        f.BoostAmount = (float)(60 + 30 * Math.Sin(_raceClock / 20));
        f.BoostActive = throttle > 0.95;
        f.BrakeBias = 0.565f;
        f.AntiLockSetting = -1;
        f.TractionControlSetting = -1;
        f.LaunchStage = LaunchStage.Invalid;
        f.ClutchTemp = 340;
        f.Wings[0] = 0.55f;
        f.Wings[1] = 0.62f;
    }
}
