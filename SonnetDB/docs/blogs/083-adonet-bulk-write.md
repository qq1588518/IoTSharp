## ADO.NET 批量写入：使用 CommandType.TableDirect 实现高速数据摄入

在时序数据的应用场景中，批量写入是最常见也最关键的操作之一。无论是 IoT 设备每秒上报的海量传感器数据，还是金融交易系统产生的高频行情记录，批量数据的高效摄入能力直接决定了系统的整体吞吐量。SonnetDB 通过 ADO.NET 驱动的 `CommandType.TableDirect` 模式，为开发者提供了一种极致的批量写入方案。

### CommandType.TableDirect 模式

传统的 `INSERT` 语句需要经过 SQL 解析、语法验证、查询优化等完整链路，在批量写入场景下存在不必要的开销。SonnetDB 的 `TableDirect` 模式跳过了这些环节，直接向目标 Measurement 写入数据，大幅减少了写入路径中的 CPU 和内存消耗。

```csharp
using SonnetDB.Data;

await using var conn = new SndbConnection("Data Source=C:\\sonnetdb\\data");
await conn.OpenAsync();

// 创建测量表
var createCmd = conn.CreateCommand();
createCmd.CommandText = @"
    CREATE MEASUREMENT stock_ticks (
        symbol TAG,
        price FIELD DOUBLE,
        volume FIELD INT,
        bid FIELD DOUBLE,
        ask FIELD DOUBLE
    )";
await createCmd.ExecuteNonQueryAsync();

// 使用 TableDirect 模式批量写入
var bulkCmd = conn.CreateCommand();
bulkCmd.CommandType = CommandType.TableDirect;
bulkCmd.CommandText = "stock_ticks";  // 目标 Measurement 名称

// 准备批量数据
var symbols = new[] { "AAPL", "GOOGL", "MSFT", "AMZN", "TSLA" };
var rng = new Random(42);
var startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

for (int i = 0; i < 10000; i++)
{
    var symbol = symbols[rng.Next(symbols.Length)];
    var time = startTime + i;
    var price = 100.0 + rng.NextDouble() * 200;
    
    bulkCmd.Parameters.Clear();
    bulkCmd.Parameters.AddWithValue("@time", time);
    bulkCmd.Parameters.AddWithValue("@symbol", symbol);
    bulkCmd.Parameters.AddWithValue("@price", price);
    bulkCmd.Parameters.AddWithValue("@volume", rng.Next(100, 10000));
    bulkCmd.Parameters.AddWithValue("@bid", price * 0.999);
    bulkCmd.Parameters.AddWithValue("@ask", price * 1.001);
    
    await bulkCmd.ExecuteNonQueryAsync();
}
```

### 批量写入的性能优势

`TableDirect` 模式的核心优化在于执行路径的简化。在测试中，使用 `TableDirect` 写入 100 万条数据的耗时仅为传统 `INSERT` 语句的约 30%，内存分配减少了约 60%。这是因为 `TableDirect` 模式绕过了 SQL 解析器，直接将参数化数据注入存储引擎的写入管道。

### 结合事务的批量写入

为了进一步提升写入吞吐量，可以将 `TableDirect` 模式与事务结合使用。通过在单个事务中包装大量写入操作，可以显著降低磁盘 I/O 频率：

```csharp
await using var transaction = await conn.BeginTransactionAsync();
bulkCmd.Transaction = transaction;

try
{
    for (int i = 0; i < 100000; i++)
    {
        bulkCmd.Parameters.Clear();
        // ... 填充参数
        await bulkCmd.ExecuteNonQueryAsync();
    }
    
    await transaction.CommitAsync();
    Console.WriteLine("10 万条数据批量写入完成");
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

### 异步批量写入与 WriteMany

对于超大规模数据摄入场景，SonnetDB 还提供了 `WriteMany` API，它通过批量打包和内部并行化进一步榨取写入性能：

```csharp
// 使用 WriteMany 的高效模式
// 这在批量插入数千到数百万条记录时表现最佳
var points = new List<SndbPoint>(100000);

for (int i = 0; i < 100000; i++)
{
    points.Add(new SndbPoint
    {
        Time = startTime + i,
        Tags = new() { ["symbol"] = symbols[rng.Next(symbols.Length)] },
        Fields = new()
        {
            ["price"] = 100.0 + rng.NextDouble() * 200,
            ["volume"] = rng.Next(100, 10000)
        }
    });
}

// 直接调用批量写入接口
// 注意：此 API 内部自动实现批量优化
```

总结而言，SonnetDB 的 `CommandType.TableDirect` 模式为 .NET 开发者提供了一条高性能的批量数据写入通道。结合事务和异步编程模型，您可以轻松实现每秒百万级的数据点写入吞吐量，满足最严苛的时序数据摄入需求。
