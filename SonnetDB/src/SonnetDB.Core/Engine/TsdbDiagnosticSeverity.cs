namespace SonnetDB.Engine;

/// <summary>
/// SonnetDB 诊断事件严重级别。
/// </summary>
public enum TsdbDiagnosticSeverity
{
    /// <summary>提示信息。</summary>
    Information = 0,

    /// <summary>警告信息。</summary>
    Warning = 1,

    /// <summary>错误信息。</summary>
    Error = 2,
}
