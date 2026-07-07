using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace IoTSharp.Numerics
{
 
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct BigEndianUInt16 : IEquatable<BigEndianUInt16>
    {

        [MarshalAs(UnmanagedType.Struct, SizeConst = 1)]
        InlineArray2<byte>   data;
        public static implicit operator ushort(BigEndianUInt16 d)
        {
            return BinaryPrimitives.ReadUInt16BigEndian(d.data);
        }
        public static implicit operator BigEndianUInt16(ushort d)
        {
            BigEndianUInt16 bigEndianUInt16 = new BigEndianUInt16();
            BinaryPrimitives.WriteUInt16BigEndian(bigEndianUInt16.data, d);
            return bigEndianUInt16;
        }

        public readonly bool Equals(BigEndianUInt16 other) => data[0] == other.data[0] && data[1] == other.data[1];
        public override bool Equals(object? other) => ((other as BigEndianUInt16? != null)) ? Equals((BigEndianUInt16)other) : false;
        public readonly override int GetHashCode() => data[0].GetHashCode() ^ data[1].GetHashCode();
        public readonly override string? ToString() => Convert.ToHexString(data);

        public static bool operator ==(BigEndianUInt16 a, BigEndianUInt16 b) => (ushort)a == (ushort)b;
        public static bool operator !=(BigEndianUInt16 a, BigEndianUInt16 b) => (ushort)a != (ushort)b;

        public static implicit operator BigEndianUInt16(byte[] d) => MemoryMarshal.AsRef<BigEndianUInt16>(d);
        public static implicit operator byte[](BigEndianUInt16 d) => MemoryMarshal.AsBytes(new ReadOnlySpan<BigEndianUInt16>(ref d)).ToArray();
    }


}


