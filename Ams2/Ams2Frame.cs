namespace SectorTelemetry.Ams2;

using static Ams2Constants;

public sealed class ParticipantInfo
{
    public bool IsActive;
    public string Name = "";
    public float[] WorldPosition = new float[VecMax];
    public float CurrentLapDistance;
    public uint RacePosition;
    public uint LapsCompleted;
    public uint CurrentLap;
    public int CurrentSector;
}

/// <summary>
/// One complete copy of the AMS2 shared memory block. Field names follow SharedMemory.h without the "m" prefix.
/// Per-tyre arrays are indexed FL, FR, RL, RR. Per-participant arrays are indexed by participant index.
/// </summary>
public sealed class Ams2Frame
{
    // Version
    public uint Version;
    public uint BuildVersionNumber;

    // Game states
    public GameState GameState;
    public SessionState SessionState;
    public RaceState RaceState;

    // Participants
    public int ViewedParticipantIndex;
    public int NumParticipants;
    public ParticipantInfo[] Participants = NewArray<ParticipantInfo>(StoredParticipantsMax);

    // Unfiltered input
    public float UnfilteredThrottle;
    public float UnfilteredBrake;
    public float UnfilteredSteering;
    public float UnfilteredClutch;

    // Vehicle / event
    public string CarName = "";
    public string CarClassName = "";
    public uint LapsInEvent;
    public string TrackLocation = "";
    public string TrackVariation = "";
    public float TrackLength;

    // Timings
    public int NumSectors;
    public bool LapInvalidated;
    public float BestLapTime;
    public float LastLapTime;
    public float CurrentTime;
    public float SplitTimeAhead;
    public float SplitTimeBehind;
    public float SplitTime;
    public float EventTimeRemaining; // milliseconds
    public float PersonalFastestLapTime;
    public float WorldFastestLapTime;
    public float[] CurrentSectorTime = new float[3];
    public float[] FastestSectorTime = new float[3];
    public float[] PersonalFastestSectorTime = new float[3];
    public float[] WorldFastestSectorTime = new float[3];

    // Flags / pits
    public FlagColour HighestFlagColour;
    public uint HighestFlagReason;
    public PitMode PitMode;
    public PitSchedule PitSchedule;

    // Car state
    public CarFlags CarFlags;
    public float OilTempCelsius;
    public float OilPressureKPa;
    public float WaterTempCelsius;
    public float WaterPressureKPa;
    public float FuelPressureKPa;
    public float FuelLevel; // 0..1
    public float FuelCapacity; // litres
    public float Speed; // m/s
    public float Rpm;
    public float MaxRpm;
    public float Brake;
    public float Throttle;
    public float Clutch;
    public float Steering;
    public int Gear;
    public int NumGears;
    public float OdometerKm;
    public bool AntiLockActive;
    public int LastOpponentCollisionIndex;
    public float LastOpponentCollisionMagnitude;
    public bool BoostActive;
    public float BoostAmount;

    // Motion
    public float[] Orientation = new float[VecMax];
    public float[] LocalVelocity = new float[VecMax];
    public float[] WorldVelocity = new float[VecMax];
    public float[] AngularVelocity = new float[VecMax];
    public float[] LocalAcceleration = new float[VecMax];
    public float[] WorldAcceleration = new float[VecMax];
    public float[] ExtentsCentre = new float[VecMax];

    // Wheels / tyres
    public TyreFlags[] TyreFlags = new TyreFlags[TyreMax];
    public uint[] Terrain = new uint[TyreMax];
    public float[] TyreY = new float[TyreMax];
    public float[] TyreRps = new float[TyreMax];
    public float[] TyreTemp = new float[TyreMax]; // Celsius
    public float[] TyreHeightAboveGround = new float[TyreMax];
    public float[] TyreWear = new float[TyreMax];
    public float[] BrakeDamage = new float[TyreMax];
    public float[] SuspensionDamage = new float[TyreMax];
    public float[] BrakeTempCelsius = new float[TyreMax];
    public float[] TyreTreadTemp = new float[TyreMax]; // Kelvin
    public float[] TyreLayerTemp = new float[TyreMax]; // Kelvin
    public float[] TyreCarcassTemp = new float[TyreMax]; // Kelvin
    public float[] TyreRimTemp = new float[TyreMax]; // Kelvin
    public float[] TyreInternalAirTemp = new float[TyreMax]; // Kelvin

    // Damage
    public CrashState CrashState;
    public float AeroDamage;
    public float EngineDamage;

    // Weather
    public float AmbientTemperature;
    public float TrackTemperature;
    public float RainDensity;
    public float WindSpeed;
    public float WindDirectionX;
    public float WindDirectionY;
    public float CloudBrightness;

    public uint SequenceNumber;

    // PCars2 additions
    public float[] WheelLocalPositionY = new float[TyreMax];
    public float[] SuspensionTravel = new float[TyreMax];
    public float[] SuspensionVelocity = new float[TyreMax];
    public float[] AirPressure = new float[TyreMax];
    public float EngineSpeed; // rad/s
    public float EngineTorque; // Nm
    public float[] Wings = new float[2];
    public float HandBrake;

    // Per-participant race data
    public float[][] CurrentSectorTimes = NewSectorArrays();
    public float[][] FastestSectorTimes = NewSectorArrays();
    public float[] FastestLapTimes = new float[StoredParticipantsMax];
    public float[] LastLapTimes = new float[StoredParticipantsMax];
    public bool[] LapsInvalidated = new bool[StoredParticipantsMax];
    public RaceState[] RaceStates = new RaceState[StoredParticipantsMax];
    public PitMode[] PitModes = new PitMode[StoredParticipantsMax];
    public float[][] Orientations = NewVecArrays();
    public float[] Speeds = new float[StoredParticipantsMax]; // m/s
    public string[] CarNames = NewStrings(StoredParticipantsMax);
    public string[] CarClassNames = NewStrings(StoredParticipantsMax);

    public int EnforcedPitStopLap;
    public string TranslatedTrackLocation = "";
    public string TranslatedTrackVariation = "";
    public float BrakeBias;
    public float TurboBoostPressure;
    public string[] TyreCompound = NewStrings(TyreMax);
    public PitSchedule[] PitSchedules = new PitSchedule[StoredParticipantsMax];
    public FlagColour[] HighestFlagColours = new FlagColour[StoredParticipantsMax];
    public uint[] HighestFlagReasons = new uint[StoredParticipantsMax];
    public uint[] Nationalities = new uint[StoredParticipantsMax];
    public float SnowDensity;

    // AMS2 additions
    public float SessionDuration; // minutes, 0 = lap race
    public int SessionAdditionalLaps;
    public float[] TyreTempLeft = new float[TyreMax]; // Celsius
    public float[] TyreTempCenter = new float[TyreMax];
    public float[] TyreTempRight = new float[TyreMax];
    public DrsState DrsState;
    public float[] RideHeight = new float[TyreMax]; // cm
    public uint JoyPad0;
    public uint DPad;
    public int AntiLockSetting;
    public int TractionControlSetting;
    public ErsDeploymentMode ErsDeploymentMode;
    public bool ErsAutoModeEnabled;
    public float ClutchTemp; // Kelvin
    public float ClutchWear;
    public bool ClutchOverheated;
    public bool ClutchSlipping;
    public YellowFlagState YellowFlagState;
    public bool SessionIsPrivate;
    public LaunchStage LaunchStage;

    private static T[] NewArray<T>(int n) where T : new()
    {
        var a = new T[n];
        for (int i = 0; i < n; i++) a[i] = new T();
        return a;
    }

    private static string[] NewStrings(int n) => Enumerable.Repeat("", n).ToArray();
    private static float[][] NewSectorArrays() => [new float[StoredParticipantsMax], new float[StoredParticipantsMax], new float[StoredParticipantsMax]];
    private static float[][] NewVecArrays() => Enumerable.Range(0, StoredParticipantsMax).Select(_ => new float[VecMax]).ToArray();
}
