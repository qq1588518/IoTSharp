using System.Buffers.Binary;
using SonnetDB.Engine;
using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Storage.Format;
using SonnetDB.Storage.Segments;
using Xunit;

namespace SonnetDB.Core.Tests.Storage.Segments;

public sealed class SegmentAggregateSketchSidecarTests : IDisposable
{
    private readonly string _tempDir;

    public SegmentAggregateSketchSidecarTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sndb-aidx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void WriteFrom_WithNumericBlock_EmbedsAggregateSketch()
    {
        const ulong SeriesId = 0xA11CEUL;
        const string FieldName = "usage";

        var mt = new MemTable();
        long lsn = 1L;
        for (int i = 0; i < 100; i++)
        {
            double value = i % 10;
            mt.Append(SeriesId, i + 1L, FieldName, FieldValue.FromDouble(value), lsn++);
        }

        string path = Path.Combine(_tempDir, "aggregate.SDBSEG");
        var writer = new SegmentWriter(new SegmentWriterOptions { FsyncOnCommit = false });
        writer.WriteFrom(mt, segmentId: 1L, path);

        Assert.False(File.Exists(TsdbPaths.AggregateIndexPathForSegment(path)));

        using var reader = SegmentReader.Open(path);
        var block = Assert.Single(reader.FindBySeriesAndField(SeriesId, FieldName));
        Assert.False(reader.AggregateSketchOffsetsLoaded);

        Assert.True(reader.TryGetAggregateSketch(block, out var sketch));
        Assert.True(reader.AggregateSketchOffsetsLoaded);
        Assert.True(reader.AggregateSketchOffsetsEmbedded);
        Assert.Equal(block.Index, sketch.BlockIndex);
        Assert.Equal(block.Crc32, sketch.BlockCrc32);
        Assert.Equal(100L, sketch.ValueCount);
        Assert.NotNull(sketch.TDigest);
        Assert.NotNull(sketch.HyperLogLog);
        Assert.InRange(sketch.TDigest!.Quantile(0.95d), 8d, 9d);
        Assert.InRange(sketch.HyperLogLog!.Estimate(), 8L, 12L);
    }

    [Fact]
    public void TryGetAggregateSketch_WithEmbeddedSketch_ReturnsTrueWithoutSidecar()
    {
        const ulong SeriesId = 0xBEEFUL;
        const string FieldName = "usage";

        var mt = new MemTable();
        long lsn = 1L;
        for (int i = 0; i < 10; i++)
            mt.Append(SeriesId, 1_000L + i, FieldName, FieldValue.FromLong(i), lsn++);

        string path = Path.Combine(_tempDir, "embedded-aidx.SDBSEG");
        var writer = new SegmentWriter(new SegmentWriterOptions { FsyncOnCommit = false });
        writer.WriteFrom(mt, segmentId: 2L, path);
        File.Delete(TsdbPaths.AggregateIndexPathForSegment(path));

        using var reader = SegmentReader.Open(path);
        var block = Assert.Single(reader.FindBySeriesAndField(SeriesId, FieldName));

        Assert.True(reader.TryGetAggregateSketch(block, out var sketch));
        Assert.True(reader.AggregateSketchOffsetsLoaded);
        Assert.True(reader.AggregateSketchOffsetsEmbedded);
        Assert.Equal(10L, sketch.ValueCount);
        Assert.Equal(10, reader.DecodeBlock(block).Length);
    }

    [Fact]
    public void TryGetAggregateSketch_WithLegacyV5Sidecar_LoadsFallback()
    {
        const ulong SeriesId = 0xC0FFEEUL;
        const string FieldName = "usage";

        var mt = new MemTable();
        var points = new DataPoint[20];
        long lsn = 1L;
        for (int i = 0; i < points.Length; i++)
        {
            var value = FieldValue.FromDouble(i % 5);
            mt.Append(SeriesId, 2_000L + i, FieldName, value, lsn++);
            points[i] = new DataPoint(2_000L + i, value);
        }

        string path = Path.Combine(_tempDir, "legacy-aidx.SDBSEG");
        var writer = new SegmentWriter(new SegmentWriterOptions { FsyncOnCommit = false });
        writer.WriteFrom(mt, segmentId: 3L, path);
        RewriteSegmentAsV5WithoutEmbeddedExtensions(path);

        using (var readerWithoutSidecar = SegmentReader.Open(path))
        {
            var block = Assert.Single(readerWithoutSidecar.FindBySeriesAndField(SeriesId, FieldName));
            Assert.False(readerWithoutSidecar.TryGetAggregateSketch(block, out _));
            Assert.False(readerWithoutSidecar.AggregateSketchOffsetsEmbedded);
            Assert.True(BlockAggregateSketch.TryBuild(block.Index, block.Crc32, block.FieldType, points, out var sketch));
            SegmentAggregateSketchFile.Write(TsdbPaths.AggregateIndexPathForSegment(path), [sketch]);
        }

        using var reader = SegmentReader.Open(path);
        var fallbackBlock = Assert.Single(reader.FindBySeriesAndField(SeriesId, FieldName));

        Assert.True(reader.TryGetAggregateSketch(fallbackBlock, out var loaded));
        Assert.True(reader.AggregateSketchOffsetsLoaded);
        Assert.False(reader.AggregateSketchOffsetsEmbedded);
        Assert.Equal(20L, loaded.ValueCount);
    }

    private static void RewriteSegmentAsV5WithoutEmbeddedExtensions(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int footerStart = bytes.Length - FormatSizes.SegmentFooterSize;
        int indexCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(footerStart + 12, 4));
        long indexOffset = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(footerStart + 16, 8));
        long indexEnd = indexOffset + (long)indexCount * FormatSizes.BlockIndexEntrySize;
        int newLength = checked((int)(indexEnd + FormatSizes.SegmentFooterSize));

        byte[] rewritten = new byte[newLength];
        bytes.AsSpan(0, checked((int)indexEnd)).CopyTo(rewritten);
        bytes.AsSpan(footerStart, FormatSizes.SegmentFooterSize)
            .CopyTo(rewritten.AsSpan(newLength - FormatSizes.SegmentFooterSize));

        BinaryPrimitives.WriteInt32LittleEndian(rewritten.AsSpan(8, 4), 5);
        rewritten.AsSpan(36, FormatSizes.SegmentHeaderSize - 36).Clear();
        int newFooterStart = newLength - FormatSizes.SegmentFooterSize;
        BinaryPrimitives.WriteInt32LittleEndian(rewritten.AsSpan(newFooterStart + 8, 4), 5);
        BinaryPrimitives.WriteInt64LittleEndian(rewritten.AsSpan(newFooterStart + 24, 8), newLength);

        File.WriteAllBytes(path, rewritten);
    }
}
