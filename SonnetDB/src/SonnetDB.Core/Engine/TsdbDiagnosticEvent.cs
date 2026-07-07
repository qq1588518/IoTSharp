namespace SonnetDB.Engine;

/// <summary>
/// SonnetDB 引擎诊断事件。
/// </summary>
/// <param name="Operation">触发诊断的内部操作名称。</param>
/// <param name="Severity">诊断严重级别。</param>
/// <param name="Message">面向诊断的简短说明。</param>
/// <param name="Exception">相关异常；没有异常时为 null。</param>
public sealed record TsdbDiagnosticEvent(
    string Operation,
    TsdbDiagnosticSeverity Severity,
    string Message,
    Exception? Exception);
