using System.Buffers;
using System.IO.Hashing;
using SonnetDB.IO;
using SonnetDB.Storage.Format;

namespace SonnetDB.Wal;

/// <summary>
/// WAL（Write-Ahead Log）读取器，支持顺序回放，对文件尾截断和 CRC 校验失败优雅停止。
/// </summary>
public sealed class WalReader : IDisposable
{
    private FileStream? _fileStream;
    private bool _disposed;

    /// <summary>WAL 文件的完整路径。</summary>
    public string Path { get; }

    /// <summary>文件头中记录的首条 LSN。</summary>
    public long FirstLsn { get; private set; }

    /// <summary>
    /// 回放完成后最后一条合法记录的 LSN；若无合法记录则等于 <see cref="FirstLsn"/> - 1。
    /// </summary>
    public long LastLsn { get; private set; }

    /// <summary>已读取的字节数（含文件头）。</summary>
    public long BytesRead { get; private set; }

    /// <summary>
    /// 最后一条合法记录之后的文件偏移量，供未来 WAL 修复使用。
    /// </summary>
    public long LastValidOffset { get; private set; }

    private WalReader(string path, FileStream fileStream, long firstLsn)
    {
        Path = path;
        _fileStream = fileStream;
        FirstLsn = firstLsn;
        LastLsn = firstLsn - 1;
        BytesRead = FormatSizes.WalFileHeaderSize;
        LastValidOffset = FormatSizes.WalFileHeaderSize;
    }

    /// <summary>
    /// 打开一个 WAL 文件用于读取。
    /// </summary>
    /// <param name="path">WAL 文件路径。</param>
    /// <returns>已初始化的 <see cref="WalReader"/> 实例。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> 为 null。</exception>
    /// <exception cref="InvalidDataException">文件头 magic 或版本不合法时抛出。</exception>
    public static WalReader Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        try
        {
            byte[] headerBuf = ArrayPool<byte>.Shared.Rent(FormatSizes.WalFileHeaderSize);
            try
            {
                int read = ReadExact(fs, headerBuf, 0, FormatSizes.WalFileHeaderSize);
                if (read < FormatSizes.WalFileHeaderSize)
                    throw new InvalidDataException("WAL file is truncated: file header incomplete.");

                var reader = new SpanReader(headerBuf.AsSpan(0, FormatSizes.WalFileHeaderSize));
                var fileHeader = reader.ReadStruct<WalFileHeader>();

                if (!fileHeader.IsValid())
                    throw new InvalidDataException("WAL file header is invalid: magic or version mismatch.");

                return new WalReader(path, fs, fileHeader.FirstLsn);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(headerBuf);
            }
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 顺序回放所有合法记录。遇到截断或 CRC 校验失败时停止迭代，不抛出异常。
    /// </summary>
    /// <returns>按 LSN 顺序的 <see cref="WalRecord"/> 序列。</returns>
    /// <exception cref="ObjectDisposedException">读取器已关闭时抛出。</exception>
    public IEnumerable<WalRecord> Replay()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WalReader));

        _fileStream!.Position = FormatSizes.WalFileHeaderSize;
        BytesRead = FormatSizes.WalFileHeaderSize;
        LastValidOffset = FormatSizes.WalFileHeaderSize;
        LastLsn = FirstLsn - 1;

        byte[] headerBuf = ArrayPool<byte>.Shared.Rent(FormatSizes.WalRecordHeaderSize);
        try
        {
            while (true)
            {
                // Read record header
                int headerRead = ReadExact(_fileStream, headerBuf, 0, FormatSizes.WalRecordHeaderSize);
                if (headerRead < FormatSizes.WalRecordHeaderSize)
                    yield break;

                ReadOnlySpan<byte> headerSpan = headerBuf.AsSpan(0, FormatSizes.WalRecordHeaderSize);
                var headerReader = new SpanReader(headerSpan);
                var header = headerReader.ReadStruct<WalRecordHeader>();

                if (!header.IsShapeValid(headerSpan))
                    yield break;

                if (header.PayloadLength < 0)
                    yield break;

                if (header.PayloadLength > _fileStream.Length - _fileStream.Position)
                    yield break;

                byte[] payloadBuf = ArrayPool<byte>.Shared.Rent(Math.Max(header.PayloadLength, 1));
                try
                {
                    int payloadRead = header.PayloadLength > 0
                        ? ReadExact(_fileStream, payloadBuf, 0, header.PayloadLength)
                        : 0;

                    if (payloadRead < header.PayloadLength)
                        yield break;

                    // Verify CRC32
                    uint actualCrc = Crc32.HashToUInt32(payloadBuf.AsSpan(0, header.PayloadLength));
                    if (actualCrc != header.PayloadCrc32)
                        yield break;

                    // Decode payload — catch InvalidDataException without a yield inside try/catch
                    WalRecord? record = null;
                    bool shouldStop = false;
                    try
                    {
                        var payloadReader = new SpanReader(payloadBuf.AsSpan(0, header.PayloadLength));
                        record = header.RecordType switch
                        {
                            WalRecordType.WritePoint =>
                                WalPayloadCodec.ReadWritePointPayload(payloadReader, header.Lsn, header.Timestamp),
                            WalRecordType.CreateSeries =>
                                WalPayloadCodec.ReadCreateSeriesPayload(payloadReader, header.Lsn, header.Timestamp),
                            WalRecordType.Checkpoint =>
                                WalPayloadCodec.ReadCheckpointPayload(payloadReader, header.Lsn, header.Timestamp),
                            WalRecordType.Truncate =>
                                new TruncateRecord(header.Lsn, header.Timestamp),
                            WalRecordType.Delete =>
                                WalPayloadCodec.ReadDeletePayload(payloadReader, header.Lsn, header.Timestamp),
                            _ => null,
                        };
                    }
                    catch (InvalidDataException)
                    {
                        shouldStop = true;
                    }

                    // Update tracking — must happen outside the try/catch above
                    if (!shouldStop)
                    {
                        long recordSize = FormatSizes.WalRecordHeaderSize + header.PayloadLength;
                        BytesRead += recordSize;
                        LastValidOffset += recordSize;
                        LastLsn = header.Lsn;
                    }

                    if (shouldStop)
                        yield break;

                    if (record is not null)
                        yield return record;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(payloadBuf);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(headerBuf);
        }
    }

    /// <summary>
    /// 关闭读取器并释放文件句柄。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _fileStream?.Dispose();
        _fileStream = null;
    }

    private static int ReadExact(Stream stream, byte[] buffer, int offset, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read = stream.Read(buffer, offset + total, count - total);
            if (read == 0)
                break;
            total += read;
        }
        return total;
    }
}
