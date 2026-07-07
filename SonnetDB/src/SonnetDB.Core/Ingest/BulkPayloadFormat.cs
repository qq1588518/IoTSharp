namespace SonnetDB.Ingest;

/// <summary>
/// 批量入库 payload 的协议格式。
/// 用于 <see cref="BulkPayloadDetector"/> 嗅探结果以及内部解析器分发。
/// </summary>
public enum BulkPayloadFormat
{
    /// <summary>
    /// InfluxDB Line Protocol 子集：
    /// <c>measurement[,tag=val,...] field=val[,field2=val2,...] [timestamp]</c>，按 <c>\n</c> / <c>\r\n</c> 分行。
    /// </summary>
    LineProtocol = 0,

    /// <summary>
    /// JSON 数组对象：<c>{"m":"sensor_data","points":[{"t":...,"tags":{...},"fields":{...}}, ...]}</c>。
    /// </summary>
    Json = 1,

    /// <summary>
    /// PostgreSQL <c>COPY</c> 风格的 bulk INSERT VALUES：
    /// <c>INSERT INTO m(c1,c2,...) VALUES (...),(...),(...);</c>，整段只解析一次表头，后续 VALUES 走快路径。
    /// </summary>
    BulkValues = 2,
}

/// <summary>
/// 时间戳精度，用于解释批量 payload 中的整数时间戳。
/// SonnetDB 内部统一以 Unix 毫秒存储，本枚举仅用于解析阶段的换算。
/// </summary>
public enum TimePrecision
{
    /// <summary>纳秒（÷ 1_000_000 → ms）。</summary>
    Nanoseconds = 0,

    /// <summary>微秒（÷ 1_000 → ms）。</summary>
    Microseconds = 1,

    /// <summary>毫秒（默认，原值）。</summary>
    Milliseconds = 2,

    /// <summary>秒（× 1_000 → ms）。</summary>
    Seconds = 3,
}
