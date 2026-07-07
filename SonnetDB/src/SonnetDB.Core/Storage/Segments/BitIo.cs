namespace SonnetDB.Storage.Segments;

/// <summary>
/// 简易位级写入器：以 little-endian 字节序填充给定 <see cref="Span{Byte}"/>，按位追加。
/// 仅在 V2 值编码（Gorilla XOR）内部使用，写完后调用 <see cref="BytesUsed"/> 取得已用字节数。
/// </summary>
internal ref struct BitWriter
{
    private readonly Span<byte> _buffer;
    private int _bytePos;
    private int _bitInByte; // 0..7，下一个待写入的 bit 在当前字节中的位置（0=MSB）

    public BitWriter(Span<byte> buffer)
    {
        _buffer = buffer;
        _bytePos = 0;
        _bitInByte = 0;
    }

    /// <summary>写入一位（0/1）。</summary>
    public void WriteBit(int bit)
    {
        if (_bytePos >= _buffer.Length)
            throw new InvalidOperationException("BitWriter 缓冲区已满。");

        if (bit != 0)
            _buffer[_bytePos] |= (byte)(1 << (7 - _bitInByte));

        _bitInByte++;
        if (_bitInByte == 8)
        {
            _bitInByte = 0;
            _bytePos++;
        }
    }

    /// <summary>写入低 <paramref name="bits"/> 位（最多 64 位），高位优先。</summary>
    public void WriteBits(ulong value, int bits)
    {
        if ((uint)bits > 64u)
            throw new ArgumentOutOfRangeException(nameof(bits));

        for (int i = bits - 1; i >= 0; i--)
            WriteBit((int)((value >> i) & 1UL));
    }

    /// <summary>已经写入的字节数（最后一字节如有未填满的尾位也计入）。</summary>
    public int BytesUsed => _bitInByte == 0 ? _bytePos : _bytePos + 1;
}

/// <summary>
/// 简易位级读取器：从给定只读字节视图中逐位读取，搭配 <see cref="BitWriter"/> 使用。
/// </summary>
internal ref struct BitReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private int _bytePos;
    private int _bitInByte;

    public BitReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        _bytePos = 0;
        _bitInByte = 0;
    }

    /// <summary>读取一位。</summary>
    public int ReadBit()
    {
        if (_bytePos >= _buffer.Length)
            throw new InvalidDataException("BitReader 越界：尝试读取已结束的位流。");

        int bit = (_buffer[_bytePos] >> (7 - _bitInByte)) & 1;
        _bitInByte++;
        if (_bitInByte == 8)
        {
            _bitInByte = 0;
            _bytePos++;
        }
        return bit;
    }

    /// <summary>读取连续 <paramref name="bits"/> 位（最多 64），高位优先。</summary>
    public ulong ReadBits(int bits)
    {
        if ((uint)bits > 64u)
            throw new ArgumentOutOfRangeException(nameof(bits));

        ulong value = 0;
        for (int i = 0; i < bits; i++)
            value = (value << 1) | (uint)ReadBit();
        return value;
    }
}
