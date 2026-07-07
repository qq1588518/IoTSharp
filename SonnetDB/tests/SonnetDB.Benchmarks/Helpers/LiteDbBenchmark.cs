using LiteDB;

namespace SonnetDB.Benchmarks.Helpers;

internal sealed class LiteDbDataPoint
{
    [BsonId]
    public int Id { get; set; }

    public long Ts { get; set; }

    public string Host { get; set; } = string.Empty;

    public double Value { get; set; }
}

internal readonly record struct LiteDbAggregateBucket(
    long Bucket,
    double Avg,
    double Min,
    double Max,
    long Count);

internal static class LiteDbBenchmark
{
    public const string CollectionName = "sensor_data";

    public static string CreateTempPath(string scenario)
    {
        return Path.Combine(Path.GetTempPath(), $"sonnetdb_bench_litedb_{scenario}_{Guid.NewGuid():N}.db");
    }

    public static LiteDatabase Open(string path)
    {
        return new LiteDatabase($"Filename={path};Connection=direct");
    }

    public static LiteDbDataPoint[] CreatePoints(BenchmarkDataPoint[] points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var result = new LiteDbDataPoint[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            var point = points[i];
            result[i] = new LiteDbDataPoint
            {
                Id = i + 1,
                Ts = point.Timestamp,
                Host = point.Host,
                Value = point.Value,
            };
        }

        return result;
    }

    public static void InsertBulk(LiteDatabase db, LiteDbDataPoint[] points, bool ensureQueryIndexes)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(points);

        var collection = db.GetCollection<LiteDbDataPoint>(CollectionName);
        collection.InsertBulk(points, batchSize: 10_000);
        if (ensureQueryIndexes)
        {
            collection.EnsureIndex(x => x.Ts);
            collection.EnsureIndex(x => x.Host);
        }

        db.Checkpoint();
    }

    public static void DeleteDatabaseFiles(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return;

        string prefix = Path.GetFileNameWithoutExtension(path);
        foreach (string file in Directory.EnumerateFiles(directory, prefix + "*"))
            File.Delete(file);
    }

    public static List<LiteDbAggregateBucket> Aggregate1Min(IEnumerable<LiteDbDataPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var result = new List<LiteDbAggregateBucket>();
        long currentBucket = long.MinValue;
        double sum = 0;
        double min = 0;
        double max = 0;
        long count = 0;

        foreach (var point in points)
        {
            long bucket = point.Ts / 60_000L * 60_000L;
            if (count > 0 && bucket != currentBucket)
            {
                result.Add(new LiteDbAggregateBucket(currentBucket, sum / count, min, max, count));
                count = 0;
                sum = 0;
            }

            if (count == 0)
            {
                currentBucket = bucket;
                min = point.Value;
                max = point.Value;
            }
            else
            {
                min = Math.Min(min, point.Value);
                max = Math.Max(max, point.Value);
            }

            sum += point.Value;
            count++;
        }

        if (count > 0)
            result.Add(new LiteDbAggregateBucket(currentBucket, sum / count, min, max, count));

        return result;
    }
}
