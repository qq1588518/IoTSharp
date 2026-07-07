using System.Runtime.CompilerServices;
using System.Text;
using SonnetDB.IO;
using SonnetDB.Storage.Format;
using Xunit;

namespace SonnetDB.Core.Tests.Storage.Format;

/// <summary>
/// 段文件整体布局的集成测试：组装一个最小完整段并验证所有偏移、长度约束。
/// <para>
/// 布局：
/// <code>
/// [SegmentHeader 64B]         offset 0
/// [BlockHeader   80B]         offset 64
/// [FieldNameUtf8  N B]        offset 144
/// [TimestampPayload P B]      offset 144+N
/// [ValuePayload   Q B]        offset 144+N+P
/// [BlockIndexEntry 48B]       offset IndexOffset
/// [v6 Extension Area 0..N B]   offset IndexOffset+48
/// [SegmentFooter   64B]       offset FileLength-64
/// </code>
/// </para>
/// </summary>
public sealed class SegmentLayoutTests
{
    [Fact]
    public void MinimalSegment_WriteAndParse_AllFieldsMatch()
    {
        // ── 准备数据 ────────────────────────────────────────────────────────
        const long segmentId = 1L;
        const ulong seriesId = 42UL;
        const long minTs = 1_000L;
        const long maxTs = 2_000L;
        const int pointCount = 1;

        byte[] fieldNameBytes = Encoding.UTF8.GetBytes("value");
        int fieldNameLen = fieldNameBytes.Length;

        // 最小载荷：各 8 字节
        byte[] tsPayload = new byte[8];
        byte[] valPayload = new byte[8];
        new Random(42).NextBytes(tsPayload);
        new Random(43).NextBytes(valPayload);

        // ── 计算各段偏移 ─────────────────────────────────────────────────────
        int segHeaderSize = FormatSizes.SegmentHeaderSize;      // 64
        int blockHeaderSize = FormatSizes.BlockHeaderSize;      // 80
        int blockDataSize = fieldNameLen + tsPayload.Length + valPayload.Length;
        int blockTotalSize = blockHeaderSize + blockDataSize;
        long blockOffset = segHeaderSize;                       // = 64
        long indexOffset = blockOffset + blockTotalSize;
        int indexCount = 1;
        long fileLength = indexOffset
            + (long)indexCount * FormatSizes.BlockIndexEntrySize
            + FormatSizes.SegmentFooterSize;

        // ── 分配缓冲区并写入 ─────────────────────────────────────────────────
        byte[] bufferArr = new byte[fileLength];
        var span = bufferArr.AsSpan();
        var writer = new SpanWriter(span);

        // 1. SegmentHeader
        SegmentHeader segHeader = SegmentHeader.CreateNew(segmentId);
        segHeader.BlockCount = 1;
        segHeader.WriteFooterMiniCopy(indexCount, indexOffset, fileLength, indexCrc32: 0);
        writer.WriteStruct(in segHeader);
        Assert.Equal(segHeaderSize, writer.Position);

        // 2. BlockHeader
        BlockHeader blockHeader = BlockHeader.CreateNew(
            seriesId: seriesId,
            min: minTs,
            max: maxTs,
            count: pointCount,
            fieldType: FieldType.Float64,
            fieldNameLen: fieldNameLen,
            tsLen: tsPayload.Length,
            valLen: valPayload.Length);
        writer.WriteStruct(in blockHeader);

        // 3. FieldNameUtf8 + payloads
        writer.WriteBytes(fieldNameBytes);
        writer.WriteBytes(tsPayload);
        writer.WriteBytes(valPayload);

        Assert.Equal(indexOffset, writer.Position);

        // 4. BlockIndexEntry
        BlockIndexEntry indexEntry = new BlockIndexEntry
        {
            SeriesId = seriesId,
            MinTimestamp = minTs,
            MaxTimestamp = maxTs,
            FileOffset = blockOffset,
            BlockLength = blockTotalSize,
            FieldNameHash = 0,
        };
        writer.WriteStruct(in indexEntry);

        // 5. SegmentFooter
        SegmentFooter footer = SegmentFooter.CreateNew(indexCount, indexOffset, fileLength);
        writer.WriteStruct(in footer);

        Assert.Equal((int)fileLength, writer.Position);

        // ── 从头解析验证 ─────────────────────────────────────────────────────
        var reader = new SpanReader(span);

        SegmentHeader parsedSegHeader = reader.ReadStruct<SegmentHeader>();
        Assert.True(parsedSegHeader.IsValid());
        Assert.Equal(segmentId, parsedSegHeader.SegmentId);
        Assert.Equal(1, parsedSegHeader.BlockCount);

        BlockHeader parsedBlockHeader = reader.ReadStruct<BlockHeader>();
        Assert.Equal(seriesId, parsedBlockHeader.SeriesId);
        Assert.Equal(minTs, parsedBlockHeader.MinTimestamp);
        Assert.Equal(maxTs, parsedBlockHeader.MaxTimestamp);
        Assert.Equal(pointCount, parsedBlockHeader.Count);
        Assert.Equal(fieldNameLen, parsedBlockHeader.FieldNameUtf8Length);
        Assert.Equal(tsPayload.Length, parsedBlockHeader.TimestampPayloadLength);
        Assert.Equal(valPayload.Length, parsedBlockHeader.ValuePayloadLength);

        ReadOnlySpan<byte> parsedFieldName = reader.ReadBytes(fieldNameLen);
        Assert.True(parsedFieldName.SequenceEqual(fieldNameBytes));
        reader.Skip(tsPayload.Length + valPayload.Length);

        // 验证 reader 位于 IndexOffset
        Assert.Equal((int)indexOffset, reader.Position);

        BlockIndexEntry parsedIndex = reader.ReadStruct<BlockIndexEntry>();
        Assert.Equal(seriesId, parsedIndex.SeriesId);
        Assert.Equal(blockOffset, parsedIndex.FileOffset);
        Assert.Equal(blockTotalSize, parsedIndex.BlockLength);

        SegmentFooter parsedFooter = reader.ReadStruct<SegmentFooter>();
        Assert.True(parsedFooter.IsValid());
        Assert.Equal(indexCount, parsedFooter.IndexCount);
        Assert.Equal(indexOffset, parsedFooter.IndexOffset);
        Assert.Equal(fileLength, parsedFooter.FileLength);
    }

    /// <summary>
    /// 验证 v6 中 Index 区末尾可以等于或早于 Footer 起点。
    /// </summary>
    [Fact]
    public void SegmentFooter_IndexEnd_DoesNotExceedFooterStart()
    {
        // 构造最简段（SegmentHeader + 0 个 Block + 1 个索引条目 + 32B 内嵌扩展区 + Footer）
        long indexOffset = FormatSizes.SegmentHeaderSize; // 64，没有 Block
        int indexCount = 1;
        const int ExtensionBytes = 32;
        long fileLength = indexOffset
            + (long)indexCount * FormatSizes.BlockIndexEntrySize
            + ExtensionBytes
            + FormatSizes.SegmentFooterSize;

        SegmentFooter footer = SegmentFooter.CreateNew(indexCount, indexOffset, fileLength);

        long indexEnd = footer.IndexOffset + (long)footer.IndexCount * FormatSizes.BlockIndexEntrySize;
        long footerStart = footer.FileLength - FormatSizes.SegmentFooterSize;
        Assert.True(indexEnd <= footerStart);
    }

    /// <summary>
    /// 验证 BlockIndexEntry.FileOffset == sizeof(SegmentHeader)（第一个 Block 紧接 SegmentHeader）。
    /// </summary>
    [Fact]
    public void BlockIndexEntry_FirstBlock_FileOffset_EqualSegmentHeaderSize()
    {
        BlockIndexEntry entry = new BlockIndexEntry
        {
            FileOffset = FormatSizes.SegmentHeaderSize,
        };
        Assert.Equal(Unsafe.SizeOf<SegmentHeader>(), (int)entry.FileOffset);
    }
}
