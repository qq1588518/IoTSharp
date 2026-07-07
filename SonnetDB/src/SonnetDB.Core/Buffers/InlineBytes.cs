using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SonnetDB.Buffers;

/// <summary>4 字节内联缓冲区（适用于小型 magic、tag、保留字段）。</summary>
[InlineArray(Length)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct InlineBytes4
{
    /// <summary>缓冲区字节长度常量。</summary>
    public const int Length = 4;

    private byte _element0;
}

/// <summary>8 字节内联缓冲区（适用于文件 magic、ID、保留字段）。</summary>
[InlineArray(Length)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct InlineBytes8
{
    /// <summary>缓冲区字节长度常量。</summary>
    public const int Length = 8;

    private byte _element0;
}

/// <summary>16 字节内联缓冲区（适用于 GUID 字节、保留字段、小型 scratch buffer）。</summary>
[InlineArray(Length)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct InlineBytes16
{
    /// <summary>缓冲区字节长度常量。</summary>
    public const int Length = 16;

    private byte _element0;
}

/// <summary>24 字节内联缓冲区（适用于保留字段）。</summary>
[InlineArray(Length)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct InlineBytes24
{
    /// <summary>缓冲区字节长度常量。</summary>
    public const int Length = 24;

    private byte _element0;
}

/// <summary>32 字节内联缓冲区（适用于哈希、SHA256 输出、scratch buffer）。</summary>
[InlineArray(Length)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct InlineBytes32
{
    /// <summary>缓冲区字节长度常量。</summary>
    public const int Length = 32;

    private byte _element0;
}

/// <summary>64 字节内联缓冲区（适用于固定文件头保留区、cache line 对齐）。</summary>
[InlineArray(Length)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct InlineBytes64
{
    /// <summary>缓冲区字节长度常量。</summary>
    public const int Length = 64;

    private byte _element0;
}
