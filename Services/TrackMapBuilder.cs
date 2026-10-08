using System.Text.Json;
using SectorTelemetry.Ams2;

namespace SectorTelemetry.Services;

/// <summary>
/// AMS2 exposes no track geometry, so the outline is learned from where cars are: every on-track car's
/// world X/Z is averaged into buckets by lap distance. With a full field the map is complete after about one lap.
/// Maps are cached in ./tracks so later sessions on the same layout start with the full outline.
/// </summary>
public sealed class TrackMapBuilder(ILogger<TrackMapBuilder> logger)
{
    private const float BucketSize = 4f;
    private const int MaxSamplesPerBucket = 30;
    private const float OutlierDistance = 40f;
    private static readonly string CacheDir = Path.Combine(AppContext.BaseDirectory, "tracks");

    private string _key = "";
    private float _trackLength;
    private Bucket[] _buckets = [];
    private readonly BoundaryEstimate[] _sectorBoundaries = [new(), new()];
    private int _filled;
    private int _publishedFilled = -1;
    private DateTime _lastSave = DateTime.MinValue;

    public int Version { get; private set; }
    public string Key => _key;
    public double Coverage => _buckets.Length == 0 ? 0 : (double)_filled / _buckets.Length;

    private struct Bucket
    {
        public float X, Z;
        public int Count;
    }

    private sealed class BoundaryEstimate
    {
        public double Sum;
        public int Count;
        public float? Value => Count == 0 ? null : (float)(Sum / Count);
    }

    public void Update(Ams2Frame f)
    {
        if (f.TrackLength <= 0) return;
        string key = $"{f.TrackLocation}|{f.TrackVariation}";
        if (key != _key || Math.Abs(f.TrackLength - _trackLength) > 1) Reset(key, f.TrackLength);

        int n = Math.Min(f.NumParticipants, Ams2Constants.StoredParticipantsMax);
        for (int i = 0; i < n; i++)
        {
            var p = f.Participants[i];
            if (!p.IsActive || f.PitModes[i] != PitMode.None) continue;
            float d = p.CurrentLapDistance;
            if (d <= 0 || d >= _trackLength) continue;
            float x = p.WorldPosition[0], z = p.WorldPosition[2];
            if (x == 0 && z == 0) continue;

            ref var b = ref _buckets[(int)(d / BucketSize) % _buckets.Length];
            if (b.Count >= 5 && Math.Abs(b.X - x) + Math.Abs(b.Z - z) > OutlierDistance) continue;
            if (b.Count == 0) _filled++;
            int weight = Math.Min(b.Count, MaxSamplesPerBucket - 1);
            b.X = (b.X * weight + x) / (weight + 1);
            b.Z = (b.Z * weight + z) / (weight + 1);
            b.Count++;
        }

        if (_filled != _publishedFilled && (_filled - _publishedFilled > _buckets.Length / 100 || Coverage > 0.98))
        {
            _publishedFilled = _filled;
            Version++;
        }

        if (DateTime.UtcNow - _lastSave > TimeSpan.FromSeconds(30) && Coverage > 0.9) Save();
    }

    /// <summary>Called when a car moves from sector s to s+1 at the given lap distance.</summary>
    public void RecordSectorBoundary(int completedSector, float lapDistance)
    {
        if (completedSector is < 0 or > 1 || lapDistance <= 0) return;
        var est = _sectorBoundaries[completedSector];
        // Ignore samples far from the running estimate (e.g. cars rejoining from the pits).
        if (est.Count >= 3 && Math.Abs(est.Value!.Value - lapDistance) > 50) return;
        if (est.Count < 200)
        {
            est.Sum += lapDistance;
            est.Count++;
        }
    }

    public object Snapshot()
    {
        var points = new List<float[]?>(_buckets.Length);
        foreach (var b in _buckets) points.Add(b.Count == 0 ? null : [MathF.Round(b.X, 1), MathF.Round(b.Z, 1)]);
        return new
        {
            key = _key,
            version = Version,
            trackLength = _trackLength,
            bucketSize = BucketSize,
            coverage = Math.Round(Coverage, 3),
            sectorBoundaries = _sectorBoundaries.Select(s => s.Value).ToArray(),
            points,
        };
    }

    private void Reset(string key, float trackLength)
    {
        if (_key != "" && Coverage > 0.9) Save();

        _key = key;
        _trackLength = trackLength;
        _buckets = new Bucket[(int)Math.Ceiling(trackLength / BucketSize)];
        _filled = 0;
        _publishedFilled = -1;
        foreach (var s in _sectorBoundaries) { s.Sum = 0; s.Count = 0; }
        Load();
        Version++;
    }

    private string CachePath()
    {
        var safe = string.Concat(_key.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        return Path.Combine(CacheDir, $"{safe}_{(int)_trackLength}.json");
    }

    private sealed record CacheFile(float[][] Points, int[] Counts, float?[] SectorBoundaries);

    private void Load()
    {
        try
        {
            var path = CachePath();
            if (!File.Exists(path)) return;
            var cache = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(path));
            if (cache == null || cache.Points.Length != _buckets.Length) return;
            for (int i = 0; i < _buckets.Length; i++)
            {
                if (cache.Counts[i] == 0) continue;
                _buckets[i] = new Bucket { X = cache.Points[i][0], Z = cache.Points[i][1], Count = Math.Min(cache.Counts[i], MaxSamplesPerBucket) };
                _filled++;
            }
            for (int s = 0; s < 2; s++)
            {
                if (cache.SectorBoundaries[s] is float v)
                {
                    _sectorBoundaries[s].Sum = v * 10;
                    _sectorBoundaries[s].Count = 10;
                }
            }
            logger.LogInformation("Loaded cached track map {Path} ({Coverage:P0})", path, Coverage);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load cached track map");
        }
    }

    private void Save()
    {
        _lastSave = DateTime.UtcNow;
        try
        {
            Directory.CreateDirectory(CacheDir);
            var cache = new CacheFile(
                _buckets.Select(b => new[] { b.X, b.Z }).ToArray(),
                _buckets.Select(b => b.Count).ToArray(),
                _sectorBoundaries.Select(s => s.Value).ToArray());
            File.WriteAllText(CachePath(), JsonSerializer.Serialize(cache));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not save track map");
        }
    }
}
