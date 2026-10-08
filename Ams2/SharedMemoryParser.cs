using System.Buffers.Binary;
using System.Text;

namespace SectorTelemetry.Ams2;

using static Ams2Constants;

/// <summary>
/// Parses a raw copy of the AMS2 shared memory block into an <see cref="Ams2Frame"/>.
/// Reads fields in declaration order using C alignment rules (4-byte numbers, 1-byte bool/char),
/// which avoids hand-maintaining marshalling attributes for the 2D arrays in SharedMemory.h.
/// </summary>
public static class SharedMemoryParser
{
    // Offset of mSequenceNumber, used for torn-read detection without parsing the whole block.
    public const int SequenceNumberOffset = 7320;

    public static uint ReadSequenceNumber(ReadOnlySpan<byte> data) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data[SequenceNumberOffset..]);

    public static Ams2Frame Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < SharedMemorySize)
            throw new ArgumentException($"Expected {SharedMemorySize} bytes, got {data.Length}");

        var r = new Cursor(data);
        var f = new Ams2Frame();

        f.Version = r.U32();
        f.BuildVersionNumber = r.U32();

        f.GameState = (GameState)r.U32();
        f.SessionState = (SessionState)r.U32();
        f.RaceState = (RaceState)r.U32();

        f.ViewedParticipantIndex = r.I32();
        f.NumParticipants = r.I32();
        foreach (var p in f.Participants)
        {
            p.IsActive = r.Bool();
            p.Name = r.Str(StringLengthMax);
            r.Floats(p.WorldPosition);
            p.CurrentLapDistance = r.F32();
            p.RacePosition = r.U32();
            p.LapsCompleted = r.U32();
            p.CurrentLap = r.U32();
            p.CurrentSector = r.I32();
        }

        f.UnfilteredThrottle = r.F32();
        f.UnfilteredBrake = r.F32();
        f.UnfilteredSteering = r.F32();
        f.UnfilteredClutch = r.F32();

        f.CarName = r.Str(StringLengthMax);
        f.CarClassName = r.Str(StringLengthMax);

        f.LapsInEvent = r.U32();
        f.TrackLocation = r.Str(StringLengthMax);
        f.TrackVariation = r.Str(StringLengthMax);
        f.TrackLength = r.F32();

        f.NumSectors = r.I32();
        f.LapInvalidated = r.Bool();
        f.BestLapTime = r.F32();
        f.LastLapTime = r.F32();
        f.CurrentTime = r.F32();
        f.SplitTimeAhead = r.F32();
        f.SplitTimeBehind = r.F32();
        f.SplitTime = r.F32();
        f.EventTimeRemaining = r.F32();
        f.PersonalFastestLapTime = r.F32();
        f.WorldFastestLapTime = r.F32();
        r.Floats(f.CurrentSectorTime);
        r.Floats(f.FastestSectorTime);
        r.Floats(f.PersonalFastestSectorTime);
        r.Floats(f.WorldFastestSectorTime);

        f.HighestFlagColour = (FlagColour)r.U32();
        f.HighestFlagReason = r.U32();

        f.PitMode = (PitMode)r.U32();
        f.PitSchedule = (PitSchedule)r.U32();

        f.CarFlags = (CarFlags)r.U32();
        f.OilTempCelsius = r.F32();
        f.OilPressureKPa = r.F32();
        f.WaterTempCelsius = r.F32();
        f.WaterPressureKPa = r.F32();
        f.FuelPressureKPa = r.F32();
        f.FuelLevel = r.F32();
        f.FuelCapacity = r.F32();
        f.Speed = r.F32();
        f.Rpm = r.F32();
        f.MaxRpm = r.F32();
        f.Brake = r.F32();
        f.Throttle = r.F32();
        f.Clutch = r.F32();
        f.Steering = r.F32();
        f.Gear = r.I32();
        f.NumGears = r.I32();
        f.OdometerKm = r.F32();
        f.AntiLockActive = r.Bool();
        f.LastOpponentCollisionIndex = r.I32();
        f.LastOpponentCollisionMagnitude = r.F32();
        f.BoostActive = r.Bool();
        f.BoostAmount = r.F32();

        r.Floats(f.Orientation);
        r.Floats(f.LocalVelocity);
        r.Floats(f.WorldVelocity);
        r.Floats(f.AngularVelocity);
        r.Floats(f.LocalAcceleration);
        r.Floats(f.WorldAcceleration);
        r.Floats(f.ExtentsCentre);

        for (int i = 0; i < TyreMax; i++) f.TyreFlags[i] = (TyreFlags)r.U32();
        r.UInts(f.Terrain);
        r.Floats(f.TyreY);
        r.Floats(f.TyreRps);
        r.Skip(TyreMax * 4); // mTyreSlipSpeed (obsolete)
        r.Floats(f.TyreTemp);
        r.Skip(TyreMax * 4); // mTyreGrip (obsolete)
        r.Floats(f.TyreHeightAboveGround);
        r.Skip(TyreMax * 4); // mTyreLateralStiffness (obsolete)
        r.Floats(f.TyreWear);
        r.Floats(f.BrakeDamage);
        r.Floats(f.SuspensionDamage);
        r.Floats(f.BrakeTempCelsius);
        r.Floats(f.TyreTreadTemp);
        r.Floats(f.TyreLayerTemp);
        r.Floats(f.TyreCarcassTemp);
        r.Floats(f.TyreRimTemp);
        r.Floats(f.TyreInternalAirTemp);

        f.CrashState = (CrashState)r.U32();
        f.AeroDamage = r.F32();
        f.EngineDamage = r.F32();

        f.AmbientTemperature = r.F32();
        f.TrackTemperature = r.F32();
        f.RainDensity = r.F32();
        f.WindSpeed = r.F32();
        f.WindDirectionX = r.F32();
        f.WindDirectionY = r.F32();
        f.CloudBrightness = r.F32();

        f.SequenceNumber = r.U32();

        r.Floats(f.WheelLocalPositionY);
        r.Floats(f.SuspensionTravel);
        r.Floats(f.SuspensionVelocity);
        r.Floats(f.AirPressure);
        f.EngineSpeed = r.F32();
        f.EngineTorque = r.F32();
        r.Floats(f.Wings);
        f.HandBrake = r.F32();

        foreach (var s in f.CurrentSectorTimes) r.Floats(s);
        foreach (var s in f.FastestSectorTimes) r.Floats(s);
        r.Floats(f.FastestLapTimes);
        r.Floats(f.LastLapTimes);
        for (int i = 0; i < StoredParticipantsMax; i++) f.LapsInvalidated[i] = r.Bool();
        for (int i = 0; i < StoredParticipantsMax; i++) f.RaceStates[i] = (RaceState)r.U32();
        for (int i = 0; i < StoredParticipantsMax; i++) f.PitModes[i] = (PitMode)r.U32();
        foreach (var o in f.Orientations) r.Floats(o);
        r.Floats(f.Speeds);
        for (int i = 0; i < StoredParticipantsMax; i++) f.CarNames[i] = r.Str(StringLengthMax);
        for (int i = 0; i < StoredParticipantsMax; i++) f.CarClassNames[i] = r.Str(StringLengthMax);

        f.EnforcedPitStopLap = r.I32();
        f.TranslatedTrackLocation = r.Str(StringLengthMax);
        f.TranslatedTrackVariation = r.Str(StringLengthMax);
        f.BrakeBias = r.F32();
        f.TurboBoostPressure = r.F32();
        for (int i = 0; i < TyreMax; i++) f.TyreCompound[i] = r.Str(TyreCompoundNameLengthMax);
        for (int i = 0; i < StoredParticipantsMax; i++) f.PitSchedules[i] = (PitSchedule)r.U32();
        for (int i = 0; i < StoredParticipantsMax; i++) f.HighestFlagColours[i] = (FlagColour)r.U32();
        r.UInts(f.HighestFlagReasons);
        r.UInts(f.Nationalities);
        f.SnowDensity = r.F32();

        f.SessionDuration = r.F32();
        f.SessionAdditionalLaps = r.I32();
        r.Floats(f.TyreTempLeft);
        r.Floats(f.TyreTempCenter);
        r.Floats(f.TyreTempRight);
        f.DrsState = (DrsState)r.U32();
        r.Floats(f.RideHeight);
        f.JoyPad0 = r.U32();
        f.DPad = r.U32();
        f.AntiLockSetting = r.I32();
        f.TractionControlSetting = r.I32();
        f.ErsDeploymentMode = (ErsDeploymentMode)r.I32();
        f.ErsAutoModeEnabled = r.Bool();
        f.ClutchTemp = r.F32();
        f.ClutchWear = r.F32();
        f.ClutchOverheated = r.Bool();
        f.ClutchSlipping = r.Bool();
        f.YellowFlagState = (YellowFlagState)r.I32();
        f.SessionIsPrivate = r.Bool();
        f.LaunchStage = (LaunchStage)r.I32();

        r.AlignTo(4);
        if (r.Offset != SharedMemorySize)
            throw new InvalidOperationException($"Layout drift: parsed {r.Offset} bytes, expected {SharedMemorySize}");

        return f;
    }

    private ref struct Cursor(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        public int Offset { get; private set; }

        public void AlignTo(int n) => Offset = (Offset + n - 1) / n * n;
        public void Skip(int n) => Offset += n;

        public uint U32()
        {
            AlignTo(4);
            var v = BinaryPrimitives.ReadUInt32LittleEndian(_data[Offset..]);
            Offset += 4;
            return v;
        }

        public int I32()
        {
            AlignTo(4);
            var v = BinaryPrimitives.ReadInt32LittleEndian(_data[Offset..]);
            Offset += 4;
            return v;
        }

        public float F32()
        {
            AlignTo(4);
            var v = BinaryPrimitives.ReadSingleLittleEndian(_data[Offset..]);
            Offset += 4;
            return v;
        }

        public bool Bool() => _data[Offset++] != 0;

        public string Str(int length)
        {
            var bytes = _data.Slice(Offset, length);
            Offset += length;
            int end = bytes.IndexOf((byte)0);
            return Encoding.UTF8.GetString(end < 0 ? bytes : bytes[..end]);
        }

        public void Floats(float[] dest)
        {
            for (int i = 0; i < dest.Length; i++) dest[i] = F32();
        }

        public void UInts(uint[] dest)
        {
            for (int i = 0; i < dest.Length; i++) dest[i] = U32();
        }
    }
}
