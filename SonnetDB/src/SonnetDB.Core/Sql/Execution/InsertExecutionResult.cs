namespace SonnetDB.Sql.Execution;

/// <summary>
/// <c>INSERT INTO ... VALUES (...)</c> 执行结果。
/// </summary>
/// <param name="Measurement">目标 measurement 名称。</param>
/// <param name="RowsInserted">成功写入的行数（每行对应一个 <see cref="SonnetDB.Model.Point"/>）。</param>
public sealed record InsertExecutionResult(string Measurement, int RowsInserted);
