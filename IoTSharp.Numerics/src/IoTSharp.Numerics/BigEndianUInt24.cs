using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace IoTSharp.Numerics
{
 
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct BigEndianUInt24 : IEquatable<BigEndianUInt24>
    {

        [MarshalAs(UnmanagedType.Struct, SizeConst = 1)]
        InlineArray3<byte> data;

        public static implicit operator uint(BigEndianUInt24 d)
        {

            var bytes = new byte[4];
            bytes[1] = d.data[0];
            bytes[2] = d.data[1];
            bytes[3] = d.data[2];
            return BinaryPrimitives.ReadUInt32BigEndian(bytes);
        }
        public static implicit operator ulong(BigEndianUInt24 d) => (uint)d;
        public static implicit operator long(BigEndianUInt24 d) => (uint)d;

        public static implicit operator BigEndianUInt24(uint d)
        {
            BigEndianUInt24 BigEndianUInt24 = new BigEndianUInt24();
            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, d);
            BigEndianUInt24.data[0] = bytes[1];
            BigEndianUInt24.data[1] = bytes[2];
            BigEndianUInt24.data[2] = bytes[3];
            return BigEndianUInt24;
        }
        public readonly bool Equals(BigEndianUInt24 other) => data[0] == other.data[0] && data[1] == other.data[1] && data[2] == other.data[2];

        public override bool Equals(object? other) => ((other as BigEndianUInt24? != null)) ? Equals((BigEndianUInt24)other) : false;

        public readonly override int GetHashCode() => data[0].GetHashCode() ^ data[1].GetHashCode() ^ data[2].GetHashCode();
        public readonly override string? ToString() => Convert.ToHexString(data);

        public static bool operator ==(BigEndianUInt24 a, BigEndianUInt24 b) => (UInt32)a == (UInt32)b;
        public static bool operator !=(BigEndianUInt24 a, BigEndianUInt24 b) => (UInt32)a != (UInt32)b;

        public static implicit operator BigEndianUInt24(byte[] d) => MemoryMarshal.AsRef<BigEndianUInt24>(new byte[4] { d[0], d[1], d[2],0 });

        public static implicit operator byte[](BigEndianUInt24 d) => MemoryMarshal.AsBytes(new Span<BigEndianUInt24>(ref d)).Slice(0, 3).ToArray();
    }
   
}
