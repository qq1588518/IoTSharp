using System.IO.MemoryMappedFiles;

namespace SonnetDB.Storage.Segments;

internal abstract class SegmentByteSource : IDisposable
{
    public abstract long Length { get; }

    public abstract bool IsMemoryMapped { get; }

    public abstract ReadOnlySpan<byte> ReadSpan(long offset, int length);

    public abstract void Dispose();

    protected static void ValidateRange(long offset, int length, long sourceLength)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));
        if (offset > sourceLength || sourceLength - offset < length)
            throw new ArgumentOutOfRangeException(nameof(length));
    }
}

internal sealed class ByteArraySegmentByteSource : SegmentByteSource
{
    private byte[]? _bytes;

    public ByteArraySegmentByteSource(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        _bytes = bytes;
    }

    public override long Length => _bytes?.LongLength ?? 0L;

    public override bool IsMemoryMapped => false;

    public override ReadOnlySpan<byte> ReadSpan(long offset, int length)
    {
        var bytes = _bytes ?? throw new ObjectDisposedException(nameof(SegmentReader));
        ValidateRange(offset, length, bytes.Length);
        return bytes.AsSpan((int)offset, length);
    }

    public override void Dispose()
    {
        _bytes = null;
    }
}

internal sealed class MemoryMappedSegmentByteSource : SegmentByteSource
{
    private readonly long _length;
    private MemoryMappedFile? _mappedFile;
    private MemoryMappedViewAccessor? _accessor;

    private MemoryMappedSegmentByteSource(
        long length,
        MemoryMappedFile mappedFile,
        MemoryMappedViewAccessor accessor)
    {
        _length = length;
        _mappedFile = mappedFile;
        _accessor = accessor;
    }

    public override long Length => _accessor is null ? 0L : _length;

    public override bool IsMemoryMapped => true;

    public static MemoryMappedSegmentByteSource Open(string path, long length)
    {
        ArgumentNullException.ThrowIfNull(path);

        var mappedFile = MemoryMappedFile.CreateFromFile(
            path,
            FileMode.Open,
            mapName: null,
            capacity: 0L,
            MemoryMappedFileAccess.Read);
        try
        {
            var accessor = mappedFile.CreateViewAccessor(0L, 0L, MemoryMappedFileAccess.Read);
            return new MemoryMappedSegmentByteSource(length, mappedFile, accessor);
        }
        catch
        {
            mappedFile.Dispose();
            throw;
        }
    }

    public override ReadOnlySpan<byte> ReadSpan(long offset, int length)
    {
        var accessor = _accessor ?? throw new ObjectDisposedException(nameof(SegmentReader));
        ValidateRange(offset, length, _length);
        if (length == 0)
            return ReadOnlySpan<byte>.Empty;

        var buffer = new byte[length];
        int read = accessor.ReadArray(offset, buffer, 0, length);
        if (read != length)
            throw new InvalidDataException("Memory-mapped segment 读取长度不足。");

        return buffer;
    }

    public override void Dispose()
    {
        _accessor?.Dispose();
        _mappedFile?.Dispose();
        _accessor = null;
        _mappedFile = null;
    }
}
