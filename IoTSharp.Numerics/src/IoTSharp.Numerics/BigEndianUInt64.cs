using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
namespace IoTSharp.Numerics
{



    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct BigEndianUInt64 : IEquatable<BigEndianUInt64>
    {
        [MarshalAs(UnmanagedType.Struct, SizeConst = 1)]
        InlineArray8<byte> data;

        public static implicit operator ulong(BigEndianUInt64 d)
        {
            return BinaryPrimitives.ReadUInt64BigEndian(d.data);
        }
        public static implicit operator BigEndianUInt64(ulong d)
        {
            BigEndianUInt64 bigEndianUInt64 = new BigEndianUInt64();
            BinaryPrimitives.WriteUInt64BigEndian(bigEndianUInt64.data, d);
            return bigEndianUInt64;
        }
        public bool Equals(BigEndianUInt64 other) => data[0] == other.data[0] && data[1] == other.data[1] && data[2] == other.data[2] && data[3] == other.data[3] && data[4] == other.data[4] && data[5] == other.data[5] && data[6] == other.data[6] && data[7] == other.data[7];
        public override bool Equals(object? other) => ((other as BigEndianUInt64? != null)) ? Equals((BigEndianUInt64)other) : false;
        public readonly override int GetHashCode() => data[0].GetHashCode() ^ data[1].GetHashCode() ^ data[2].GetHashCode() ^ data[3].GetHashCode() ^ data[4].GetHashCode() ^ data[5].GetHashCode() ^ data[6].GetHashCode() ^ data[7].GetHashCode();
        public readonly override string? ToString() => Convert.ToHexString(data);

        public static bool operator ==(BigEndianUInt64 a, BigEndianUInt64 b) => a.Equals(b);
        public static bool operator !=(BigEndianUInt64 a, BigEndianUInt64 b) => !a.Equals(b);


        public static implicit operator BigEndianUInt64(byte[] d) => MemoryMarshal.AsRef<BigEndianUInt64>(d);
        public static implicit operator byte[](BigEndianUInt64 d) => MemoryMarshal.AsBytes(new ReadOnlySpan<BigEndianUInt64>(ref d)).ToArray();
    }


}



