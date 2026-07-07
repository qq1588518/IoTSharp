## ADO.NET 基础：使用 SndbConnection、SndbCommand 和 SndbDataReader 连接本地 SonnetDB

SonnetDB 为 .NET 生态提供了原生的 ADO.NET 驱动支持，这意味着您可以使用标准的 `System.Data` 抽象层来操作时序数据库。通过 `SndbConnection`、`SndbCommand` 和 `SndbDataReader` 三个核心类，开发者可以像操作传统关系数据库一样，在嵌入式模式下连接和查询 SonnetDB 实例，无需额外部署数据库服务。

### 嵌入式连接模式

SonnetDB 最显著的特性就是"嵌入式优先"架构。在本地嵌入式模式下，数据库引擎直接运行在您的应用程序进程中，数据文件存储在本地文件系统中。以下是一个基本的连接示例：

```csharp
using SonnetDB.Data;

// 嵌入式连接：直接指定数据目录
await using var conn = new SndbConnection("Data Source=C:\\sonnetdb\\data");
await conn.OpenAsync();

// 创建时序表（Measurement）
var cmd = conn.CreateCommand();
cmd.CommandText = @"
    CREATE MEASUREMENT sensor_data (
        device_id TAG,
        temperature FIELD DOUBLE,
        humidity FIELD DOUBLE,
        voltage FIELD DOUBLE
    )";
await cmd.ExecuteNonQueryAsync();
```

连接字符串中的 `Data Source` 参数指向 SonnetDB 的数据存储目录。如果目录不存在，驱动会自动创建。嵌入式模式下无需启动任何额外的服务进程，所有读写操作均在当前进程内完成，延迟极低。

### 执行查询与数据读取

写入数据后，您可以使用 `SndbCommand` 执行查询，并通过 `SndbDataReader` 以流式方式读取结果。这种流式读取模式对于处理大规模时序数据集尤为重要，它可以避免一次性将所有数据加载到内存中。

```csharp
// 插入示例数据
cmd.CommandText = @"
    INSERT INTO sensor_data (time, device_id, temperature, humidity, voltage)
    VALUES (1713676800000, 'sensor-01', 23.5, 65.2, 3.3),
           (1713676801000, 'sensor-01', 23.7, 64.8, 3.4)
";
await cmd.ExecuteNonQueryAsync();

// 查询数据
cmd.CommandText = @"
    SELECT time, temperature, humidity
    FROM sensor_data
    WHERE device_id = 'sensor-01'
      AND time >= 1713676800000
    ORDER BY time DESC
    LIMIT 100
";

await using var reader = await cmd.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    var time = reader.GetInt64(0);
    var temp = reader.GetDouble(1);
    var hum = reader.GetDouble(2);
    Console.WriteLine($"时间: {time}, 温度: {temp}, 湿度: {hum}");
}
```

### 参数化查询与安全

与传统的 ADO.NET 驱动一样，SonnetDB 支持参数化查询，这不仅能防止 SQL 注入攻击，还能提升查询性能（查询计划可重用）。SonnetDB 使用 `@` 前缀命名参数：

```csharp
cmd.CommandText = @"
    SELECT time, temperature FROM sensor_data
    WHERE device_id = @deviceId
      AND time >= @startTime
      AND time < @endTime
";

cmd.Parameters.AddWithValue("@deviceId", "sensor-01");
cmd.Parameters.AddWithValue("@startTime", 1713676800000);
cmd.Parameters.AddWithValue("@endTime", 1713763200000);

await using var reader = await cmd.ExecuteReaderAsync();
// 处理结果集...
```

### 事务支持

SonnetDB 的 ADO.NET 驱动完整支持事务，确保批量写入操作的原子性：

```csharp
await using var transaction = await conn.BeginTransactionAsync();
try
{
    cmd.Transaction = transaction;
    
    for (int i = 0; i < 1000; i++)
    {
        cmd.CommandText = $"INSERT INTO sensor_data ...";
        await cmd.ExecuteNonQueryAsync();
    }
    
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
}
```

总结而言，SonnetDB 的 ADO.NET 实现遵循您熟悉的 `System.Data` 规范，学习成本极低。嵌入式模式让您无需维护额外服务即可获得高性能的时序数据存储能力，是边缘计算和桌面应用的理想选择。
