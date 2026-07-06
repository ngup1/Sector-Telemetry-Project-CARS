using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace SectorTelemetry;

[SupportedOSPlatform("windows")]
public class Program
{
    public static void Main()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("This app only runs on Windows.");
            return;
        }

        const string mapName = "$pcars2$";

        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(
                mapName,
                MemoryMappedFileRights.Read);

            int size = Marshal.SizeOf<SharedMemoryPartial>();
            byte[] buffer = new byte[size];

            using var accessor = mmf.CreateViewAccessor(
                0,
                size,
                MemoryMappedFileAccess.Read);

            while (true)
            {
                accessor.ReadArray(0, buffer, 0, buffer.Length);

                var data = ByteArrayToStruct<SharedMemoryPartial>(buffer);

                Console.WriteLine($"Opened mapping {mapName}");
                Console.WriteLine($"Read {buffer.Length} bytes");
                Console.WriteLine($"mVersion: {data.mVersion}");
                Console.WriteLine($"mBuildVersionNumber: {data.mBuildVersionNumber}");
                Console.WriteLine($"mGameState: {data.mGameState}");
                Console.WriteLine($"mSessionState: {data.mSessionState}");
                Console.WriteLine($"mRaceState: {data.mRaceState}");
                Console.WriteLine($"mViewedParticipantIndex: {data.mViewedParticipantIndex}");
                Console.WriteLine($"mNumParticipants: {data.mNumParticipants}");
                Console.WriteLine($"mCarName: {data.mCarName}");
                Console.WriteLine($"mTrackLocation: {data.mTrackLocation}");
                Console.WriteLine($"mTrackVariation: {data.mTrackVariation}");
                Console.WriteLine($"mSpeed: {data.mSpeed:F2} m/s");
                Console.WriteLine($"mSpeed: {data.mSpeed * 3.6f:F2} km/h");
                Console.WriteLine($"mRpm: {data.mRpm:F0}");
                Console.WriteLine($"mGear: {data.mGear}");
                Console.WriteLine($"mThrottle: {data.mThrottle:F3}");
                Console.WriteLine($"mBrake: {data.mBrake:F3}");
                Console.WriteLine($"mSteering: {data.mSteering:F3}");
                Console.WriteLine($"mUnfilteredThrottle: {data.mUnfilteredThrottle:F3}");
                Console.WriteLine($"mUnfilteredBrake: {data.mUnfilteredBrake:F3}");
                Console.WriteLine($"mUnfilteredSteering: {data.mUnfilteredSteering:F3}");
                Console.WriteLine($"mFuelLevel: {data.mFuelLevel:F3}");
                Console.WriteLine($"mFuelCapacity: {data.mFuelCapacity:F2}");

                Thread.Sleep(100);
            }
           
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    private static T ByteArrayToStruct<T>(byte[] bytes) where T : struct
    {
        IntPtr ptr = Marshal.AllocHGlobal(bytes.Length);

        try
        {
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            return Marshal.PtrToStructure<T>(ptr)!;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}