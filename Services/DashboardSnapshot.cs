using SectorTelemetry.Ams2;

namespace SectorTelemetry.Services;

/// <summary>Builds the JSON-ready object pushed to dashboard clients on every tick.</summary>
public static class DashboardSnapshot
{
    public static object Build(Ams2Frame f, TimingTracker timing, TrackMapBuilder map, string status)
    {
        int n = Math.Min(f.NumParticipants, Ams2Constants.StoredParticipantsMax);
        bool isRace = f.SessionState is SessionState.Race or SessionState.FormationLap;
        var active = Enumerable.Range(0, Math.Max(n, 0)).Where(i => f.Participants[i].IsActive).ToList();
        var order = active
            .OrderBy(i => f.Participants[i].RacePosition == 0 ? int.MaxValue : (int)f.Participants[i].RacePosition)
            .ToList();
        int leader = order.Count > 0 ? order[0] : -1;

        var cars = new List<object>(order.Count);
        for (int k = 0; k < order.Count; k++)
        {
            int i = order[k];
            int ahead = k > 0 ? order[k - 1] : -1;
            cars.Add(Car(f, timing, i, leader, ahead, isRace));
        }

        return new
        {
            type = "tick",
            status,
            clock = R(timing.Clock, 2),
            trackVersion = map.Version,
            trackKey = map.Key,
            trackCoverage = R(map.Coverage, 3),
            session = Session(f, timing, leader),
            player = f.ViewedParticipantIndex >= 0 && f.ViewedParticipantIndex < n ? Player(f, timing, f.ViewedParticipantIndex) : null,
            cars,
        };
    }

    public static object StatusOnly(string status) => new { type = "status", status };

    private static object Session(Ams2Frame f, TimingTracker timing, int leader)
    {
        bool timed = f.SessionDuration > 0 || (f.LapsInEvent == 0 && f.EventTimeRemaining > 0);
        return new
        {
            gameState = f.GameState.ToString(),
            sessionState = f.SessionState.ToString(),
            raceState = f.RaceState.ToString(),
            track = string.IsNullOrEmpty(f.TranslatedTrackLocation) ? f.TrackLocation : f.TranslatedTrackLocation,
            variation = string.IsNullOrEmpty(f.TranslatedTrackVariation) ? f.TrackVariation : f.TranslatedTrackVariation,
            trackLength = R(f.TrackLength, 1),
            numSectors = f.NumSectors,
            timed,
            lapsInEvent = f.LapsInEvent,
            additionalLaps = f.SessionAdditionalLaps,
            durationMinutes = R(f.SessionDuration, 1),
            timeRemaining = f.EventTimeRemaining > 0 ? R(f.EventTimeRemaining / 1000.0, 1) : null,
            leaderLap = leader >= 0 ? (int)f.Participants[leader].CurrentLap : 0,
            flag = f.HighestFlagColour.ToString(),
            flagReason = f.HighestFlagReason,
            yellowFlagState = f.YellowFlagState.ToString(),
            enforcedPitStopLap = f.EnforcedPitStopLap,
            sessionBest = new
            {
                lap = T(timing.SessionBestLap),
                lapBy = timing.SessionBestLapIndex >= 0 ? f.Participants[timing.SessionBestLapIndex].Name : null,
                sectors = timing.SessionBestSectors.Select(T).ToArray(),
            },
            weather = new
            {
                ambient = R(f.AmbientTemperature, 1),
                track = R(f.TrackTemperature, 1),
                rain = R(f.RainDensity, 2),
                snow = R(f.SnowDensity, 2),
                windSpeed = R(f.WindSpeed, 1),
                windDirX = R(f.WindDirectionX, 2),
                windDirY = R(f.WindDirectionY, 2),
                cloud = R(f.CloudBrightness, 2),
            },
        };
    }

    private static object Car(Ams2Frame f, TimingTracker timing, int i, int leader, int ahead, bool isRace)
    {
        var p = f.Participants[i];
        var c = timing.Car(i);

        double? gap = null, interval = null;
        int gapLaps = 0, intervalLaps = 0;
        if (isRace)
        {
            if (leader >= 0 && leader != i) (gap, gapLaps) = timing.Gap(leader, i);
            if (ahead >= 0) (interval, intervalLaps) = timing.Gap(ahead, i);
        }
        else
        {
            if (c.BestLap > 0 && timing.SessionBestLap > 0 && i != timing.SessionBestLapIndex) gap = c.BestLap - timing.SessionBestLap;
            float aheadBest = ahead >= 0 ? timing.Car(ahead).BestLap : -1;
            if (c.BestLap > 0 && aheadBest > 0) interval = c.BestLap - aheadBest;
        }

        var sectors = new object[3];
        for (int s = 0; s < 3; s++)
        {
            bool current = c.Cur[s] > 0;
            float t = current ? c.Cur[s] : c.Prev[s];
            sectors[s] = new { t = T(t), c = timing.SectorColour(i, s, t), prev = !current && t > 0 };
        }

        return new
        {
            idx = i,
            pos = p.RacePosition,
            name = p.Name,
            car = f.CarNames[i],
            cls = f.CarClassNames[i],
            isPlayer = i == f.ViewedParticipantIndex,
            lap = p.CurrentLap,
            lapsCompleted = p.LapsCompleted,
            lapDist = R(p.CurrentLapDistance, 1),
            sector = c.Sector,
            x = R(p.WorldPosition[0], 2),
            z = R(p.WorldPosition[2], 2),
            heading = R(f.Orientations[i][1], 3),
            speed = R(f.Speeds[i] * 3.6, 1),
            gap = R(gap, 3),
            gapLaps,
            interval = R(interval, 3),
            intervalLaps,
            last = T(c.LastLap),
            lastColour = timing.LapColour(i, c.LastLap),
            best = T(c.BestLap),
            bestColour = timing.LapColour(i, c.BestLap),
            sectors,
            invalid = c.CurInvalid,
            laps = c.Laps.Count,
            pit = f.PitModes[i].ToString(),
            pitSchedule = f.PitSchedules[i].ToString(),
            pitStops = c.PitStops,
            lastPitTime = T(c.LastPitLaneTime),
            state = f.RaceStates[i].ToString(),
            flag = f.HighestFlagColours[i].ToString(),
        };
    }

    private static object Player(Ams2Frame f, TimingTracker timing, int i)
    {
        var c = timing.Car(i);
        float fuelLitres = f.FuelLevel * f.FuelCapacity;
        float? perLap = timing.FuelPerLap;

        var tyres = new object[Ams2Constants.TyreMax];
        for (int w = 0; w < Ams2Constants.TyreMax; w++)
        {
            tyres[w] = new
            {
                compound = f.TyreCompound[w],
                surface = R(f.TyreTemp[w], 1),
                // Left/centre/right across the tread, as seen from behind the car.
                left = R(f.TyreTempLeft[w], 1),
                centre = R(f.TyreTempCenter[w], 1),
                right = R(f.TyreTempRight[w], 1),
                tread = K(f.TyreTreadTemp[w]),
                carcass = K(f.TyreCarcassTemp[w]),
                rim = K(f.TyreRimTemp[w]),
                air = K(f.TyreInternalAirTemp[w]),
                pressurePsi = R(Psi(f.AirPressure[w]), 1),
                wear = R(f.TyreWear[w], 4),
                brakeTemp = R(f.BrakeTempCelsius[w], 0),
                brakeDamage = R(f.BrakeDamage[w], 3),
                suspensionDamage = R(f.SuspensionDamage[w], 3),
                rideHeight = R(f.RideHeight[w], 2),
                suspensionTravel = R(f.SuspensionTravel[w] * 1000, 1),
                terrain = f.Terrain[w],
                onGround = f.TyreFlags[w].HasFlag(TyreFlags.IsOnGround),
                attached = f.TyreFlags[w].HasFlag(TyreFlags.Attached),
                inflated = f.TyreFlags[w].HasFlag(TyreFlags.Inflated),
            };
        }

        var sectors = new object[3];
        for (int s = 0; s < 3; s++)
            sectors[s] = new { t = T(c.Cur[s]), c = timing.SectorColour(i, s, c.Cur[s]), best = T(c.Best[s]) };

        return new
        {
            idx = i,
            name = f.Participants[i].Name,
            car = f.CarName,
            cls = f.CarClassName,
            speed = R(f.Speed * 3.6, 1),
            rpm = R(f.Rpm, 0),
            maxRpm = R(f.MaxRpm, 0),
            gear = f.Gear,
            numGears = f.NumGears,
            throttle = R(f.Throttle, 3),
            brake = R(f.Brake, 3),
            clutch = R(f.Clutch, 3),
            steering = R(f.Steering, 3),
            handbrake = R(f.HandBrake, 3),
            raw = new
            {
                throttle = R(f.UnfilteredThrottle, 3),
                brake = R(f.UnfilteredBrake, 3),
                clutch = R(f.UnfilteredClutch, 3),
                steering = R(f.UnfilteredSteering, 3),
            },
            torque = R(f.EngineTorque, 0),
            odometer = R(f.OdometerKm, 1),
            lapDist = R(f.Participants[i].CurrentLapDistance, 1),
            flags = new
            {
                engineOn = f.CarFlags.HasFlag(CarFlags.EngineActive),
                engineWarning = f.CarFlags.HasFlag(CarFlags.EngineWarning),
                pitLimiter = f.CarFlags.HasFlag(CarFlags.SpeedLimiter),
                abs = f.CarFlags.HasFlag(CarFlags.Abs),
                tcs = f.CarFlags.HasFlag(CarFlags.Tcs),
                scs = f.CarFlags.HasFlag(CarFlags.Scs),
                headlight = f.CarFlags.HasFlag(CarFlags.Headlight),
                absActive = f.AntiLockActive,
            },
            drs = new
            {
                installed = f.DrsState.HasFlag(DrsState.Installed),
                zoneRules = f.DrsState.HasFlag(DrsState.ZoneRules),
                availableNext = f.DrsState.HasFlag(DrsState.AvailableNext),
                availableNow = f.DrsState.HasFlag(DrsState.AvailableNow),
                active = f.DrsState.HasFlag(DrsState.Active),
            },
            ers = new
            {
                mode = f.ErsDeploymentMode.ToString(),
                auto = f.ErsAutoModeEnabled,
                boostActive = f.BoostActive,
                boostAmount = R(f.BoostAmount, 1),
            },
            setup = new
            {
                brakeBias = R(f.BrakeBias, 3),
                absSetting = f.AntiLockSetting,
                tcSetting = f.TractionControlSetting,
                turboBoost = R(f.TurboBoostPressure, 2),
                frontWing = R(f.Wings[0], 2),
                rearWing = R(f.Wings[1], 2),
                launch = f.LaunchStage.ToString(),
            },
            engine = new
            {
                oilTemp = R(f.OilTempCelsius, 1),
                oilPressure = R(f.OilPressureKPa, 0),
                waterTemp = R(f.WaterTempCelsius, 1),
                waterPressure = R(f.WaterPressureKPa, 0),
                fuelPressure = R(f.FuelPressureKPa, 0),
            },
            clutchState = new
            {
                temp = K(f.ClutchTemp),
                wear = R(f.ClutchWear, 3),
                overheated = f.ClutchOverheated,
                slipping = f.ClutchSlipping,
            },
            fuel = new
            {
                litres = R(fuelLitres, 2),
                capacity = R(f.FuelCapacity, 1),
                level = R(f.FuelLevel, 4),
                perLap = R(perLap, 3),
                lapsLeft = perLap > 0 ? R(fuelLitres / perLap.Value, 1) : null,
            },
            damage = new
            {
                aero = R(f.AeroDamage, 3),
                engine = R(f.EngineDamage, 3),
                crash = f.CrashState.ToString(),
            },
            pit = new
            {
                mode = f.PitMode.ToString(),
                schedule = f.PitSchedule.ToString(),
            },
            timing = new
            {
                current = T(f.CurrentTime),
                last = T(c.LastLap),
                lastColour = timing.LapColour(i, c.LastLap),
                best = T(c.BestLap),
                invalid = f.LapInvalidated,
                delta = new { best = R(timing.DeltaToBest, 3), last = R(timing.DeltaToLast, 3) },
                referenceLap = new
                {
                    best = timing.BestTrace is { } b ? T(b.LapTime) : null,
                    last = timing.LastTrace is { } l ? T(l.LapTime) : null,
                },
                splitAhead = T(f.SplitTimeAhead),
                splitBehind = T(f.SplitTimeBehind),
                sectors,
                sector = c.Sector,
            },
            tyres,
            laps = c.Laps.TakeLast(30).Select(LapDto),
        };
    }

    public static object CarLaps(TimingTracker timing, int i)
    {
        var c = timing.Car(i);
        return new { idx = i, name = c.Name, best = T(c.BestLap), laps = c.Laps.Select(LapDto) };
    }

    private static object LapDto(LapRecord l) => new
    {
        lap = l.Lap,
        time = T(l.Time),
        sectors = l.Sectors.Select(T),
        invalid = l.Invalid,
        pitted = l.Pitted,
        fuelUsed = R(l.FuelUsed, 2),
    };

    // AMS2 documents PSI, but values in the kPa range have been reported; the ranges don't overlap.
    private static double Psi(float v) => v > 60 ? v * 0.1450377 : v;

    /// <summary>Rounded value, or null for NaN/infinity (which JSON can't carry).</summary>
    private static double? R(double? v, int digits) =>
        v is { } d && double.IsFinite(d) ? Math.Round(d, digits) : null;

    /// <summary>A time in seconds, or null when unset (the game uses -1 / 0 for "no time").</summary>
    private static double? T(float t) => t > 0 && float.IsFinite(t) ? Math.Round(t, 3) : null;

    private static double? K(float kelvin) => kelvin > 0 ? R(kelvin - 273.15, 1) : null;
}
