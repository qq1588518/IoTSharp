using System.Buffers.Binary;
using SonnetDB.Model;
using SonnetDB.Storage.Format;
using SonnetDB.Wal;
using Xunit;

namespace SonnetDB.Core.Tests.Wal;

/// <summary>
/// <see cref="WalReader"/> 单元测试。
/// </summary>
public sealed class WalReaderTests : IDisposable
{
    private readonly string _tempDir;

    public WalReaderTests()
    {
        _tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string TempFile() => System.IO.Path.Combine(_tempDir, System.IO.Path.GetRandomFileName() + ".SDBWAL");

    private static void RemoveLastLsnFooter(string path)
    {
        using var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite);
        fs.SetLength(fs.Length - FormatSizes.WalLastLsnFooterSize);
    }

    private static void CorruptSecondRecordCrc(string path)
    {
        long secondCrcOffset = GetRecordOffset(path, 1) + 12;
        using var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite);
        fs.Position = secondCrcOffset;
        int b = fs.ReadByte();
        fs.Position = secondCrcOffset;
        fs.WriteByte((byte)(b ^ 0xFF));
    }

    private static long GetRecordOffset(string path, int recordIndex)
    {
        using var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read);
        long offset = FormatSizes.WalFileHeaderSize;
        Span<byte> intBuf = stackalloc byte[sizeof(int)];

        for (int i = 0; i < recordIndex; i++)
        {
            fs.Position = offset + 8;
            ReadExact(fs, intBuf);
            int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(intBuf);
            offset += FormatSizes.WalRecordHeaderSize + payloadLength;
        }

        return offset;
    }

    private static void CorruptPayloadByte(string path, long recordOffset)
    {
        using var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite);
        fs.Position = recordOffset + FormatSizes.WalRecordHeaderSize;
        int b = fs.ReadByte();
        fs.Position = recordOffset + FormatSizes.WalRecordHeaderSize;
        fs.WriteByte((byte)(b ^ 0xFF));
    }

    private static void CorruptPayloadLength(string path, long recordOffset)
    {
        using var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite);
        fs.Position = recordOffset + 8;
        Span<byte> intBuf = stackalloc byte[sizeof(int)];
        ReadExact(fs, intBuf);
        int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(intBuf);
        BinaryPrimitives.WriteInt32LittleEndian(intBuf, payloadLength + 1);
        fs.Position = recordOffset + 8;
        fs.Write(intBuf);
    }

    private static void ClearRecordHeaderChecksums(string path)
    {
        using var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite);
        long offset = FormatSizes.WalFileHeaderSize;
        Span<byte> intBuf = stackalloc byte[sizeof(int)];

        while (offset + FormatSizes.WalRecordHeaderSize <= fs.Length)
        {
            fs.Position = offset + 8;
            ReadExact(fs, intBuf);
            int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(intBuf);
            if (payloadLength < 0 || offset + FormatSizes.WalRecordHeaderSize + payloadLength > fs.Length)
                return;

            fs.Position = offset + 5;
            fs.WriteByte(0);
            fs.WriteByte(0);
            fs.WriteByte(0);

            offset += FormatSizes.WalRecordHeaderSize + payloadLength;
        }
    }

    private static void AssertReplayStoppedAtFirstRecord(string path, long expectedLastValidOffset)
    {
        using var reader = WalReader.Open(path);
        var records = reader.Replay().ToList();

        Assert.Single(records);
        Assert.IsType<CreateSeriesRecord>(records[0]);
        Assert.Equal(1L, reader.LastLsn);
        Assert.Equal(expectedLastValidOffset, reader.LastValidOffset);
    }

    private static void ReadExact(System.IO.Stream stream, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer[total..]);
            if (read == 0)
                throw new EndOfStreamException();
            total += read;
        }
    }

    private static void WriteMixedRecords(string path, int count)
    {
        using var writer = WalWriter.Open(path);
        var tags = new Dictionary<string, string> { ["host"] = "srv1" };
        writer.AppendCreateSeries(1UL, "cpu", tags);
        for (int i = 0; i < count - 1; i++)
            writer.AppendWritePoint(1UL, 1000L + i, "usage", FieldValue.FromDouble(i * 0.1));
        writer.Sync();
    }

    [Fact]
    public void RoundTrip_100MixedRecords_AllRead()
    {
        string path = TempFile();
        WriteMixedRecords(path, 100);

        var records = new List<WalRecord>();
        using var reader = WalReader.Open(path);
        records.AddRange(reader.Replay());

        Assert.Equal(100, records.Count);
        Assert.IsType<CreateSeriesRecord>(records[0]);
        for (int i = 1; i < 100; i++)
            Assert.IsType<WritePointRecord>(records[i]);
    }

    [Fact]
    public void TruncatedFile_StopsWithoutThrow()
    {
        string path = TempFile();
        WriteMixedRecords(path, 10);
        RemoveLastLsnFooter(path);

        // Truncate last 5 bytes
        var fi = new System.IO.FileInfo(path);
        using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite))
            fs.SetLength(fi.Length - 5);

        var records = new List<WalRecord>();
        Exception? ex = null;
        try
        {
            using var reader = WalReader.Open(path);
            records.AddRange(reader.Replay());
        }
        catch (Exception e) { ex = e; }

        Assert.Null(ex);
        Assert.Equal(9, records.Count);
    }

    [Fact]
    public void CorruptedLastPayload_CrcFailure_StopsWithoutThrow()
    {
        string path = TempFile();
        WriteMixedRecords(path, 10);

        // Corrupt the last byte of the file (payload of last record)
        using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite))
        {
            fs.Position = fs.Length - FormatSizes.WalLastLsnFooterSize - 1;
            int b = fs.ReadByte();
            fs.Position = fs.Length - FormatSizes.WalLastLsnFooterSize - 1;
            fs.WriteByte((byte)(b ^ 0xFF));
        }

        var records = new List<WalRecord>();
        Exception? ex = null;
        try
        {
            using var reader = WalReader.Open(path);
            records.AddRange(reader.Replay());
        }
        catch (Exception e) { ex = e; }

        Assert.Null(ex);
        Assert.Equal(9, records.Count);
    }

    [Fact]
    public void CorruptedMiddleRecord_CrcFailure_StopsAtPreviousRecord()
    {
        string path = TempFile();
        WriteMixedRecords(path, 10);

        CorruptSecondRecordCrc(path);

        using var reader = WalReader.Open(path);
        var records = reader.Replay().ToList();

        Assert.Single(records);
        Assert.IsType<CreateSeriesRecord>(records[0]);
        Assert.Equal(1L, reader.LastLsn);
    }

    [Fact]
    public void Replay_WithPayloadTruncatedAtSecondRecord_StopsAtPreviousRecord()
    {
        string path = TempFile();
        WriteMixedRecords(path, 3);
        RemoveLastLsnFooter(path);
        long secondRecordOffset = GetRecordOffset(path, 1);

        using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite))
            fs.SetLength(secondRecordOffset + FormatSizes.WalRecordHeaderSize + 3);

        AssertReplayStoppedAtFirstRecord(path, secondRecordOffset);
    }

    [Fact]
    public void Replay_WithHeaderTruncatedAtSecondRecord_StopsAtPreviousRecord()
    {
        string path = TempFile();
        WriteMixedRecords(path, 3);
        RemoveLastLsnFooter(path);
        long secondRecordOffset = GetRecordOffset(path, 1);

        using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite))
            fs.SetLength(secondRecordOffset + 5);

        AssertReplayStoppedAtFirstRecord(path, secondRecordOffset);
    }

    [Fact]
    public void Replay_WithDamagedLengthAtSecondRecord_StopsAtPreviousRecord()
    {
        string path = TempFile();
        WriteMixedRecords(path, 3);
        RemoveLastLsnFooter(path);
        long secondRecordOffset = GetRecordOffset(path, 1);

        CorruptPayloadLength(path, secondRecordOffset);

        AssertReplayStoppedAtFirstRecord(path, secondRecordOffset);
    }

    [Fact]
    public void Replay_WithPayloadCrcErrorAtSecondRecord_StopsAtPreviousRecord()
    {
        string path = TempFile();
        WriteMixedRecords(path, 3);
        RemoveLastLsnFooter(path);
        long secondRecordOffset = GetRecordOffset(path, 1);

        CorruptPayloadByte(path, secondRecordOffset);

        AssertReplayStoppedAtFirstRecord(path, secondRecordOffset);
    }

    [Fact]
    public void Replay_WithLegacyHeaderWithoutChecksum_ReadsAllRecords()
    {
        string path = TempFile();
        WriteMixedRecords(path, 3);
        RemoveLastLsnFooter(path);
        ClearRecordHeaderChecksums(path);

        using var reader = WalReader.Open(path);
        var records = reader.Replay().ToList();

        Assert.Equal(3, records.Count);
        Assert.Equal(new[] { 1L, 2L, 3L }, records.Select(static r => r.Lsn).ToArray());
    }

    [Fact]
    public void InvalidMagicHeader_ThrowsInvalidDataException()
    {
        string path = TempFile();
        WriteMixedRecords(path, 5);

        // Corrupt the file header magic
        using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite))
        {
            fs.Position = 0;
            fs.WriteByte(0xFF);
        }

        Assert.Throws<InvalidDataException>(() => WalReader.Open(path));
    }

    [Fact]
    public void LastValidOffset_IsCorrectAfterReplay()
    {
        string path = TempFile();
        WriteMixedRecords(path, 5);

        using var reader = WalReader.Open(path);
        var records = reader.Replay().ToList();

        Assert.Equal(5, records.Count);
        // LastValidOffset should be at the end of the 5th record
        Assert.Equal(reader.BytesRead, reader.LastValidOffset);
        Assert.True(reader.LastValidOffset > FormatSizes.WalFileHeaderSize);
    }
}
