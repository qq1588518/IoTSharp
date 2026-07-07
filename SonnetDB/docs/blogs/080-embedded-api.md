## 嵌入式 C# API：Tsdb.Open() 与 SqlExecutor.Execute() 进程内数据库编程

SonnetDB 首先是一个 .NET 10 时序数据库引擎，其次才是一个网络服务。这意味着您可以在自己的 C# 应用程序中直接引用 SonnetDB，以嵌入式模式运行——无需独立部署服务进程，无需 HTTP 调用，所有操作在进程内完成。

### 嵌入式架构概览

嵌入式使用的核心 API 只有两个：

```
TsdbRegistry         → 数据库注册表（创建/打开数据库）
SqlExecutor          → SQL 执行器（执行任意 SQL 语句）
```

不需要启动 ASP.NET 服务，不需要配置 HTTP 端点，直接在你的业务代码中调用即可。

### 打开或创建数据库

```csharp
using SonnetDB.Hosting;
using SonnetDB.Sql.Execution;

// 指定数据存储根目录
var registry = new TsdbRegistry(@"C:\data\sonnetdb");

// 打开已有数据库，如果不存在则创建
if (registry.TryCreate("sensor_metrics", out var tsdb))
{
    Console.WriteLine("数据库已就绪");
}

// 或者只读打开已存在的数据库
if (registry.TryGet("sensor_metrics", out var existingDb))
{
    Console.WriteLine("打开已有数据库");
}
```

### 执行 SQL 语句

`SqlExecutor.ExecuteStatement()` 接受一个 `Tsdb` 实例和 `SqlStatement` AST，返回强类型执行结果：

```csharp
using SonnetDB.Sql.Ast;
using SonnetDB.Sql.Execution;

// 建表
SqlExecutor.ExecuteStatement(tsdb, new CreateMeasurementStatement(
    "cpu_usage",
    [
        new ColumnDefinition("host", ColumnKind.Tag, SqlDataType.String),
        new ColumnDefinition("cpu_pct", ColumnKind.Field, SqlDataType.Float),
    ]));

// 写入数据
SqlExecutor.ExecuteStatement(tsdb, new InsertStatement(
    "cpu_usage",
    ["host", "cpu_pct", "time"],
    [[
        LiteralExpression.String("web-01"),
        LiteralExpression.Float(78.5),
        LiteralExpression.Integer(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
    ]]));

// 查询数据
var result = SqlExecutor.ExecuteStatement(tsdb, new SelectStatement(
    [new SelectItem(StarExpression.Instance, null)],
    "cpu_usage",
    Where: null,
    GroupBy: []));

if (result is SelectExecutionResult selectResult)
{
    foreach (var row in selectResult.Rows)
    {
        Console.WriteLine($"Host: {row[1]}, CPU: {row[2]}");
    }
}
```

### SQL 解析与安全

`SqlParser.Parse()` 将 SQL 文本解析为 AST，然后在 `SqlExecutor` 中执行。这保证了所有 SQL 都经过语法校验，避免注入风险：

```csharp
using SonnetDB.Sql;

var statement = SqlParser.Parse("SELECT avg(cpu_pct) FROM cpu_usage WHERE time >= now() - 1h");
var result = SqlExecutor.ExecuteStatement(tsdb, statement);
```

### 执行计划估算

对于只读查询，`explainSqlService.Explain()` 可以估算将会扫描的段数和行数，方便做容量规划：

```csharp
var plan = explainSqlService.Explain("sensor_metrics", tsdb, statement);
Console.WriteLine($"预计扫描 {plan.SegmentCount} 个段、{plan.EstimatedRowCount} 行");
```

### 应用场景

**IoT 边缘计算**：在边缘设备上直接嵌入 SonnetDB，采集传感器数据后即时聚合分析，无需回传云端。

**游戏服务器**：在游戏进程内记录玩家行为时序数据，用于实时排行榜和反作弊检测。

**量化交易**：在交易引擎进程中嵌入时序数据库，毫秒级存储和分析行情数据。

**CI/CD 测试**：在集成测试中使用嵌入式 SonnetDB 验证数据管道逻辑，无需准备外部数据库服务。

### 轻量化部署

嵌入式模式下不需要任何外部依赖。NuGet 包引用后即可使用：

```xml
<PackageReference Include="SonnetDB" Version="10.0.*" />
```

配合 Copilot AI，您甚至可以在嵌入式应用中直接获得 AI 辅助的数据分析能力——让 Copilot 用自然语言帮您查询和分析时序数据，全部在进程内完成。
