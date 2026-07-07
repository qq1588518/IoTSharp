using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SonnetDB.Configuration;

namespace SonnetDB.Copilot;

/// <summary>
/// 内置零依赖 embedding provider：基于 SHA-256 + 词袋哈希投影生成 384 维确定性向量。
/// 不依赖任何外部模型文件或网络，作为 Copilot 子系统的默认/降级实现，
/// 让"开箱即用"路径无需用户提前下载 ONNX 模型。
/// 召回质量等价于关键词匹配（语义不及真实模型），适合本地开发与离线场景。
/// </summary>
public sealed class BuiltinHashEmbeddingProvider : IEmbeddingProvider
{
    /// <summary>
    /// 内置向量维度，与 bge-small 系列保持一致以便后续无缝切换。
    /// </summary>
    public const int VectorDimension = 384;

    private readonly CopilotEmbeddingOptions _options;

    /// <summary>
    /// 构造内置 embedding provider。
    /// </summary>
    /// <param name="options">embedding 配置（仅用于读取 Provider 字段以记录降级原因）。</param>
    public BuiltinHashEmbeddingProvider(CopilotEmbeddingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>
    /// 当前 provider 是否处于"降级"状态（用户配置了 local 但模型缺失，回落到 builtin）。
    /// </summary>
    public bool IsFallback => !string.Equals(_options.Provider, "builtin", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public ValueTask<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Embedding input cannot be empty.", nameof(text));

        cancellationToken.ThrowIfCancellationRequested();

        var vector = new float[VectorDimension];
        var tokens = Tokenize(text);
        if (tokens.Count == 0)
        {
            return ValueTask.FromResult(vector);
        }

        Span<byte> hash = stackalloc byte[32];
        foreach (var token in tokens)
        {
            var bytes = Encoding.UTF8.GetBytes(token);
            SHA256.HashData(bytes, hash);

            // 把 32 字节哈希拆成 8 个 uint32：前 4 个决定槽位，后 4 个决定权重符号/幅值。
            for (var i = 0; i < 4; i++)
            {
                var slot = (int)(BinaryPrimitives.ReadUInt32LittleEndian(hash[(i * 4)..]) % VectorDimension);
                var weightBits = BinaryPrimitives.ReadUInt32LittleEndian(hash[((i + 4) * 4)..]);
                var sign = (weightBits & 1u) == 0 ? 1f : -1f;
                vector[slot] += sign;
            }
        }

        // L2 归一化，便于下游使用余弦相似度。
        var norm = 0d;
        for (var i = 0; i < vector.Length; i++)
            norm += vector[i] * vector[i];

        if (norm > 0)
        {
            var scale = (float)(1d / Math.Sqrt(norm));
            for (var i = 0; i < vector.Length; i++)
                vector[i] *= scale;
        }

        return ValueTask.FromResult(vector);
    }

    private static List<string> Tokenize(string text)
    {
        var result = new List<string>(capacity: 16);
        var buffer = new StringBuilder(capacity: 32);

        foreach (var rune in text.EnumerateRunes())
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(rune.Value);
            var isCjk = (rune.Value >= 0x4E00 && rune.Value <= 0x9FFF) ||
                        (rune.Value >= 0x3400 && rune.Value <= 0x4DBF);

            if (isCjk)
            {
                FlushAscii(buffer, result);
                result.Add(char.ConvertFromUtf32(rune.Value));
                continue;
            }

            if (category is UnicodeCategory.LowercaseLetter
                          or UnicodeCategory.UppercaseLetter
                          or UnicodeCategory.DecimalDigitNumber
                          or UnicodeCategory.OtherLetter)
            {
                foreach (var ch in rune.ToString())
                    buffer.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                FlushAscii(buffer, result);
            }
        }

        FlushAscii(buffer, result);
        return result;
    }

    private static void FlushAscii(StringBuilder buffer, List<string> sink)
    {
        if (buffer.Length == 0)
            return;
        sink.Add(buffer.ToString());
        buffer.Clear();
    }
}
