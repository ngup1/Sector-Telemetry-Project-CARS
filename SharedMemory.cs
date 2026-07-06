using System.Runtime.InteropServices;

namespace SectorTelemetry;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 4)]
public struct ParticipantInfo
{
    [MarshalAs(UnmanagedType.I1)]
    public bool mIsActive;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = Ams2Constants.STRING_LENGTH_MAX)]
    public string mName;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = Ams2Constants.VEC_MAX)]
    public float[] mWorldPosition;

    public float mCurrentLapDistance;
    public uint mRacePosition;
    public uint mLapsCompleted;
    public uint mCurrentLap;
    public int mCurrentSector;
}


[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 4)]
public struct SharedMemoryPartial
{
    public uint mVersion;
    public uint mBuildVersionNumber;

    public uint mGameState;
    public uint mSessionState;
    public uint mRaceState;

    public int mViewedParticipantIndex;
    public int mNumParticipants;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = Ams2Constants.STORED_PARTICIPANTS_MAX)]
    public ParticipantInfo[] mParticipantInfo;

    public float mUnfilteredThrottle;
    public float mUnfilteredBrake;
    public float mUnfilteredSteering;
    public float mUnfilteredClutch;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = Ams2Constants.STRING_LENGTH_MAX)]
    public string mCarName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = Ams2Constants.STRING_LENGTH_MAX)]
    public string mCarClassName;

    public uint mLapsInEvent;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = Ams2Constants.STRING_LENGTH_MAX)]
    public string mTrackLocation;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = Ams2Constants.STRING_LENGTH_MAX)]
    public string mTrackVariation;

    public float mTrackLength;

    public int mNumSectors;

    [MarshalAs(UnmanagedType.I1)]
    public bool mLapInvalidated;

    public float mBestLapTime;
    public float mLastLapTime;
    public float mCurrentTime;
    public float mSplitTimeAhead;
    public float mSplitTimeBehind;
    public float mSplitTime;
    public float mEventTimeRemaining;
    public float mPersonalFastestLapTime;
    public float mWorldFastestLapTime;
    public float mCurrentSector1Time;
    public float mCurrentSector2Time;
    public float mCurrentSector3Time;
    public float mFastestSector1Time;
    public float mFastestSector2Time;
    public float mFastestSector3Time;
    public float mPersonalFastestSector1Time;
    public float mPersonalFastestSector2Time;
    public float mPersonalFastestSector3Time;
    public float mWorldFastestSector1Time;
    public float mWorldFastestSector2Time;
    public float mWorldFastestSector3Time;

    public uint mHighestFlagColour;
    public uint mHighestFlagReason;

    public uint mPitMode;
    public uint mPitSchedule;

    public uint mCarFlags;
    public float mOilTempCelsius;
    public float mOilPressureKPa;
    public float mWaterTempCelsius;
    public float mWaterPressureKPa;
    public float mFuelPressureKPa;
    public float mFuelLevel;
    public float mFuelCapacity;
    public float mSpeed;
    public float mRpm;
    public float mMaxRPM;
    public float mBrake;
    public float mThrottle;
    public float mClutch;
    public float mSteering;
    public int mGear;
    public int mNumGears;
    public float mOdometerKM;

    [MarshalAs(UnmanagedType.I1)]
    public bool mAntiLockActive;

    public int mLastOpponentCollisionIndex;
    public float mLastOpponentCollisionMagnitude;

    [MarshalAs(UnmanagedType.I1)]
    public bool mBoostActive;

    public float mBoostAmount;
}