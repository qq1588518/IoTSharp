using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace IoTSharp.Numerics
{

    public static class InlineArrayExtensions
    {
        extension(byte[] values)
        {
            public InlineArray2<byte> AsIla2() => MemoryMarshal.AsRef<InlineArray2<byte>>(values);
            public InlineArray3<byte> AsIla3() => MemoryMarshal.AsRef<InlineArray3<byte>>(values);
            public InlineArray4<byte> AsIla4() => MemoryMarshal.AsRef<InlineArray4<byte>>(values);
            public InlineArray5<byte> AsIla5() => MemoryMarshal.AsRef<InlineArray5<byte>>(values);
            public InlineArray6<byte> AsIla6() => MemoryMarshal.AsRef<InlineArray6<byte>>(values);
            public InlineArray7<byte> AsIla7() => MemoryMarshal.AsRef<InlineArray7<byte>>(values);
            public InlineArray8<byte> AsIla8() => MemoryMarshal.AsRef<InlineArray8<byte>>(values);
            public InlineArray9<byte> AsIla9() => MemoryMarshal.AsRef<InlineArray9<byte>>(values);
            public InlineArray10<byte> AsIla10() => MemoryMarshal.AsRef<InlineArray10<byte>>(values);
            public InlineArray11<byte> AsIla11() => MemoryMarshal.AsRef<InlineArray11<byte>>(values);
            public InlineArray12<byte> AsIla12() => MemoryMarshal.AsRef<InlineArray12<byte>>(values);
            public InlineArray13<byte> AsIla13() => MemoryMarshal.AsRef<InlineArray13<byte>>(values);
            public InlineArray14<byte> AsIla14() => MemoryMarshal.AsRef<InlineArray14<byte>>(values);
            public InlineArray15<byte> AsIla15() => MemoryMarshal.AsRef<InlineArray15<byte>>(values);
            public InlineArray16<byte> AsIla16() => MemoryMarshal.AsRef<InlineArray16<byte>>(values);
        }
    }

}

 