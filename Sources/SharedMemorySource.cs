using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using SectorTelemetry.Ams2;

namespace SectorTelemetry.Sources;

/// <summary>Reads the live "$pcars2$" memory map written by AMS2 (Options > System > Shared Memory = Project CARS 2).</summary>
[SupportedOSPlatform("windows")]
public sealed class SharedMemorySource(ILogger<SharedMemorySource> logger) : ITelemetrySource
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(2);

    private readonly byte[] _buffer = new byte[Ams2Constants.SharedMemorySize];
    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _view;
    private DateTime _nextOpenAttempt = DateTime.MinValue;
    private uint _lastSequence = uint.MaxValue;

    public string Status { get; private set; } = "Waiting for AMS2";

    public Ams2Frame? TryRead()
    {
        if (_view == null && !TryOpen()) return null;

        try
        {
            // The game increments mSequenceNumber before and after each write: odd means a write is in progress.
            // Copy the block, then confirm the sequence number did not move during the copy.
            for (int attempt = 0; attempt < 5; attempt++)
            {
                uint before = _view!.ReadUInt32(SharedMemoryParser.SequenceNumberOffset);
                if (before % 2 != 0) continue;
                if (before == _lastSequence) return null;

                _view.ReadArray(0, _buffer, 0, _buffer.Length);
                uint after = SharedMemoryParser.ReadSequenceNumber(_buffer);
                if (after != before) continue;

                _lastSequence = before;
                var frame = SharedMemoryParser.Parse(_buffer);
                Status = frame.Version == Ams2Constants.SharedMemoryVersion
                    ? "Connected"
                    : $"Connected (shared memory v{frame.Version}, expected v{Ams2Constants.SharedMemoryVersion})";
                return frame;
            }
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Shared memory read failed; reopening");
            Close();
            return null;
        }
    }

    private bool TryOpen()
    {
        if (DateTime.UtcNow < _nextOpenAttempt) return false;
        _nextOpenAttempt = DateTime.UtcNow + RetryInterval;

        try
        {
            _mmf = MemoryMappedFile.OpenExisting(Ams2Constants.MapName, MemoryMappedFileRights.Read);
            _view = _mmf.CreateViewAccessor(0, Ams2Constants.SharedMemorySize, MemoryMappedFileAccess.Read);
            _lastSequence = uint.MaxValue;
            logger.LogInformation("Opened shared memory {Map}", Ams2Constants.MapName);
            return true;
        }
        catch (FileNotFoundException)
        {
            Status = "Waiting for AMS2 (enable Shared Memory: Project CARS 2)";
            Close();
            return false;
        }
    }

    private void Close()
    {
        _view?.Dispose();
        _mmf?.Dispose();
        _view = null;
        _mmf = null;
    }

    public void Dispose() => Close();
}
