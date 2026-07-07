namespace SonnetDB.Ingest;

/// <summary>
/// 通过 payload 前后字节嗅探批量入库协议格式。
/// 不解析内容，仅做 O(1) 前缀检查。
/// </summary>
public static class BulkPayloadDetector
{
    /// <summary>
    /// 嗅探 <paramref name="payload"/> 的协议格式。
    /// </summary>
    /// <param name="payload">待检测的整段 payload（含可能的前导/尾随空白）。</param>
    /// <returns>识别出的 <see cref="BulkPayloadFormat"/>。</returns>
    /// <remarks>
    /// 规则（按顺序）：
    /// <list type="number">
    /// <item>跳过前导空白后第一个非空白字符为 <c>{</c>，且尾随空白前最后一个非空白字符为 <c>}</c> → <see cref="BulkPayloadFormat.Json"/>。</item>
    /// <item>跳过前导空白后前 6 个字符（忽略大小写）为 <c>INSERT</c> → <see cref="BulkPayloadFormat.BulkValues"/>。</item>
    /// <item>其余一律视为 <see cref="BulkPayloadFormat.LineProtocol"/>。</item>
    /// </list>
    /// </remarks>
    public static BulkPayloadFormat Detect(ReadOnlySpan<char> payload)
    {
        int start = 0;
        int end = payload.Length;
        while (start < end && IsWhitespace(payload[start])) start++;
        while (end > start && IsWhitespace(payload[end - 1])) end--;
        if (start >= end)
            return BulkPayloadFormat.LineProtocol;

        char first = payload[start];
        if (first == '{' && payload[end - 1] == '}')
            return BulkPayloadFormat.Json;

        if (end - start >= 6 && IsInsertKeyword(payload.Slice(start, 6)))
            return BulkPayloadFormat.BulkValues;

        return BulkPayloadFormat.LineProtocol;
    }

    /// <summary>
    /// 嗅探并切分 <paramref name="commandText"/>：可选的首行 measurement 前缀 + 实际 payload。
    /// </summary>
    /// <param name="commandText">用户传入的 <c>CommandText</c>。</param>
    /// <param name="measurement">提取到的 measurement 名（无前缀时为 <c>null</c>）。</param>
    /// <param name="payload">实际 payload 片段。</param>
    /// <returns>识别出的 <see cref="BulkPayloadFormat"/>。</returns>
    /// <remarks>
    /// <para>
    /// 首行规则：第一行不含空白与等号，且后续仍有内容时，才视为 measurement 前缀。
    /// 这样 Line Protocol（含空格）与 JSON（首字符 <c>{</c>）都不会被误识别为前缀。
    /// </para>
    /// </remarks>
    public static BulkPayloadFormat DetectWithPrefix(
        string commandText,
        out string? measurement,
        out ReadOnlyMemory<char> payload)
    {
        ArgumentNullException.ThrowIfNull(commandText);
        var span = commandText.AsSpan();

        // 跳过前导空白（保留 \r\n 计数）
        int idx = 0;
        while (idx < span.Length && IsWhitespace(span[idx])) idx++;

        // 计算第一行结束位置
        int lineEnd = idx;
        while (lineEnd < span.Length && span[lineEnd] != '\n' && span[lineEnd] != '\r') lineEnd++;

        var firstLine = span.Slice(idx, lineEnd - idx);
        bool firstLineLooksLikeMeasurement =
            firstLine.Length > 0 &&
            firstLine.Length <= 255 &&
            !ContainsAny(firstLine, ' ', '\t', '=', ',', '{', '}', '(', ')', ';');

        // 必须存在「第二行」（即首行后还有非空白内容）才把首行当 measurement
        int afterFirstLine = lineEnd;
        if (afterFirstLine < span.Length && span[afterFirstLine] == '\r') afterFirstLine++;
        if (afterFirstLine < span.Length && span[afterFirstLine] == '\n') afterFirstLine++;

        bool hasSecondLine = false;
        for (int i = afterFirstLine; i < span.Length; i++)
        {
            if (!IsWhitespace(span[i])) { hasSecondLine = true; break; }
        }

        if (firstLineLooksLikeMeasurement && hasSecondLine)
        {
            measurement = new string(firstLine);
            payload = commandText.AsMemory(afterFirstLine);
        }
        else
        {
            measurement = null;
            payload = commandText.AsMemory();
        }

        return Detect(payload.Span);
    }

    private static bool IsWhitespace(char c)
        => c == ' ' || c == '\t' || c == '\r' || c == '\n';

    private static bool ContainsAny(ReadOnlySpan<char> span, char a, char b, char c, char d, char e, char f, char g, char h, char i)
    {
        for (int idx = 0; idx < span.Length; idx++)
        {
            char ch = span[idx];
            if (ch == a || ch == b || ch == c || ch == d || ch == e || ch == f || ch == g || ch == h || ch == i)
                return true;
        }
        return false;
    }

    private static bool IsInsertKeyword(ReadOnlySpan<char> six)
        => (six[0] == 'I' || six[0] == 'i')
        && (six[1] == 'N' || six[1] == 'n')
        && (six[2] == 'S' || six[2] == 's')
        && (six[3] == 'E' || six[3] == 'e')
        && (six[4] == 'R' || six[4] == 'r')
        && (six[5] == 'T' || six[5] == 't');
}
