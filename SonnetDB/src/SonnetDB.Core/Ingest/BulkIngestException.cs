namespace SonnetDB.Ingest;

/// <summary>
/// 批量入库解析过程中遇到的格式错误。
/// </summary>
public sealed class BulkIngestException : Exception
{
    /// <summary>使用错误信息构造异常。</summary>
    public BulkIngestException(string message) : base(message) { }

    /// <summary>使用错误信息与内部异常构造异常。</summary>
    public BulkIngestException(string message, Exception inner) : base(message, inner) { }
}
