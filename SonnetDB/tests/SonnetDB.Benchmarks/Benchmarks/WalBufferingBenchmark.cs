using System.Buffers;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;

namespace SonnetDB.Benchmarks.Benchmarks;

/// <summary>
/// WAL 写缓冲 microbenchmark：比较旧的 <see cref="BufferedStream"/> 包装方式与自管理写缓冲。
/// </summary>
[Config(typeof(WalBufferingConfig))]
[MemoryDiagnoser]
[BenchmarkCategory("WalBuffering")]
public class WalBufferingBenchmark
{
    private const int BufferSize = 64 * 1024;
    private byte[] _record = [];

    /// <summary>每次 benchmark 写入的 WAL-like 小记录数量。</summary>
    [Params(2_000_000)]
    public int RecordCount { get; set; }

    /// <summary>初始化固定大小的 WAL-like record。</summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        _record = new byte[96];
        for (int i = 0; i < _record.Length; i++)
            _record[i] = (byte)(i * 31);
    }

    /// <summary>旧策略：<see cref="BufferedStream"/> 包装 <see cref="FileStream"/>。</summary>
    [Benchmark(Baseline = true, Description = "BufferedStream(FileStream)")]
    public void BufferedStream_FileStream()
    {
        string path = TempWalPath();
        try
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            using var bs = new BufferedStream(fs, BufferSize);
            for (int i = 0; i < RecordCount; i++)
                bs.Write(_record);
            bs.Flush();
            fs.Flush();
        }
        finally
        {
            TryDelete(path);
        }
    }

    /// <summary>新策略：直接使用 <see cref="FileStream"/>，上层自管理固定写缓冲。</summary>
    [Benchmark(Description = "FileStream + self buffer")]
    public void FileStream_SelfManagedBuffer()
    {
        string path = TempWalPath();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        int buffered = 0;
        try
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 1);
            for (int i = 0; i < RecordCount; i++)
                WriteBuffered(fs, buffer, ref buffered, _record);

            FlushBuffered(fs, buffer, ref buffered);
            fs.Flush();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            TryDelete(path);
        }
    }

    private static void WriteBuffered(FileStream fs, byte[] buffer, ref int buffered, ReadOnlySpan<byte> source)
    {
        if (source.Length >= buffer.Length)
        {
            FlushBuffered(fs, buffer, ref buffered);
            fs.Write(source);
            return;
        }

        if (source.Length > buffer.Length - buffered)
            FlushBuffered(fs, buffer, ref buffered);

        source.CopyTo(buffer.AsSpan(buffered));
        buffered += source.Length;
    }

    private static void FlushBuffered(FileStream fs, byte[] buffer, ref int buffered)
    {
        if (buffered == 0)
            return;

        fs.Write(buffer.AsSpan(0, buffered));
        buffered = 0;
    }

    private static string TempWalPath()
        => Path.Combine(Path.GetTempPath(), $"sonnetdb_wal_buffering_{Guid.NewGuid():N}.SDBWAL");

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Benchmark cleanup best effort.
        }
    }

    private sealed class WalBufferingConfig : ManualConfig
    {
        public WalBufferingConfig()
        {
            AddJob(Job.Default
                .WithStrategy(RunStrategy.Monitoring)
                .WithWarmupCount(1)
                .WithIterationCount(5));
        }
    }
}
