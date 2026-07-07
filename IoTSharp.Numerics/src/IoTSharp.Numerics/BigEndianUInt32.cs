using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace IoTSharp.Numerics
{

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct BigEndianUInt32 : IEquatable<BigEndianUInt32>
    {
        [MarshalAs(UnmanagedType.Struct, SizeConst = 1)]
        InlineArray4<byte> data;

        public static implicit operator uint(BigEndianUInt32 d)
        {
            return BinaryPrimitives.ReadUInt32BigEndian(d.data);
        }
        public static implicit operator ulong(BigEndianUInt32 d) => (uint)d;
        public static implicit operator long(BigEndianUInt32 d) => (uint)d;

        public static implicit operator BigEndianUInt32(uint d)
        {
            BigEndianUInt32 bigEndianUInt32 = new BigEndianUInt32();
            BinaryPrimitives.WriteUInt32BigEndian(bigEndianUInt32.data, d);
            return bigEndianUInt32;
        }
        public  readonly bool Equals(BigEndianUInt32 other) => data[0] == other.data[0] && data[1] == other.data[1] && data[2] == other.data[2] && data[3] == other.data[3];
        public override bool Equals(object? other) => ((other as BigEndianUInt32? != null)) ? Equals((BigEndianUInt32)other) : false;
        public override readonly int GetHashCode() => data[0].GetHashCode() ^ data[1].GetHashCode() ^ data[2].GetHashCode() ^ data[3].GetHashCode();
        public  override readonly string? ToString() => Convert.ToHexString(data);

        public static bool operator ==(BigEndianUInt32 a, BigEndianUInt32 b) => (UInt32)a == (UInt32)b;
        public static bool operator !=(BigEndianUInt32 a, BigEndianUInt32 b) => (UInt32)a != (UInt32)b;

        public static implicit operator BigEndianUInt32(byte[] d) => MemoryMarshal.AsRef<BigEndianUInt32>(d);
        public static implicit operator byte[](BigEndianUInt32 d) => MemoryMarshal.AsBytes(new ReadOnlySpan<BigEndianUInt32>(ref d)).ToArray();
    }
   
}
