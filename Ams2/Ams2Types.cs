namespace SectorTelemetry.Ams2;

// Mirrors the enums in SharedMemory.h (SHARED_MEMORY_VERSION 14).

public static class Ams2Constants
{
    public const string MapName = "$pcars2$";
    public const uint SharedMemoryVersion = 14;
    public const int StringLengthMax = 64;
    public const int StoredParticipantsMax = 64;
    public const int TyreCompoundNameLengthMax = 40;
    public const int TyreMax = 4;
    public const int VecMax = 3;

    // sizeof(SharedMemory) as compiled from SharedMemory.h
    public const int SharedMemorySize = 20700;
}

public enum GameState : uint
{
    Exited = 0,
    FrontEnd,
    InGamePlaying,
    InGamePaused,
    InGameInMenuTimeTicking,
    InGameRestarting,
    InGameReplay,
    FrontEndReplay,
}

public enum SessionState : uint
{
    Invalid = 0,
    Practice,
    Test,
    Qualify,
    FormationLap,
    Race,
    TimeAttack,
}

public enum RaceState : uint
{
    Invalid = 0,
    NotStarted,
    Racing,
    Finished,
    Disqualified,
    Retired,
    Dnf,
}

public enum FlagColour : uint
{
    None = 0,
    Green,
    Blue,
    WhiteSlowCar,
    WhiteFinalLap,
    Red,
    Yellow,
    DoubleYellow,
    BlackAndWhite,
    BlackOrangeCircle,
    Black,
    Chequered,
}

public enum PitMode : uint
{
    None = 0,
    DrivingIntoPits,
    InPit,
    DrivingOutOfPits,
    InGarage,
    DrivingOutOfGarage,
}

public enum PitSchedule : uint
{
    None = 0,
    PlayerRequested,
    EngineerRequested,
    DamageRequested,
    Mandatory,
    DriveThrough,
    StopGo,
    PitspotOccupied,
}

[Flags]
public enum CarFlags : uint
{
    Headlight = 1 << 0,
    EngineActive = 1 << 1,
    EngineWarning = 1 << 2,
    SpeedLimiter = 1 << 3,
    Abs = 1 << 4,
    Handbrake = 1 << 5,
    Tcs = 1 << 6,
    Scs = 1 << 7,
}

[Flags]
public enum TyreFlags : uint
{
    Attached = 1 << 0,
    Inflated = 1 << 1,
    IsOnGround = 1 << 2,
}

public enum CrashState : uint
{
    None = 0,
    OffTrack,
    LargeProp,
    Spinning,
    Rolling,
}

[Flags]
public enum DrsState : uint
{
    Installed = 1 << 0,
    ZoneRules = 1 << 1,
    AvailableNext = 1 << 2,
    AvailableNow = 1 << 3,
    Active = 1 << 4,
}

public enum ErsDeploymentMode
{
    None = 0,
    Off,
    Build,
    Balanced,
    Attack,
    Qual,
}

public enum YellowFlagState
{
    Invalid = -1,
    None,
    Pending,
    PitsClosed,
    PitLeadLap,
    PitsOpen,
    PitsOpen2,
    LastLap,
    Resume,
    RaceHalt,
}

public enum LaunchStage
{
    Invalid = -1,
    Off = 0,
    Rev,
    On,
}
