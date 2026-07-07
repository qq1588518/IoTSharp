## ADO.NET 远程连接：使用 sonnetdb+http:// 协议连接远程 SonnetDB 服务

当 SonnetDB 以独立服务模式部署时，ADO.NET 驱动同样支持远程连接。通过 `sonnetdb+http://` 协议格式的连接字符串，配合 Token 认证机制，开发者可以像操作本地数据库一样远程访问 SonnetDB 服务端实例，实现分布式场景下的时序数据管理。

### 远程连接字符串格式

远程连接使用的是 HTTP/HTTPS 协议，连接字符串的格式如下：

```
sonnetdb+http://hostname:port/database?token=your_auth_token
```

其中每个部分的含义如下：

- **`sonnetdb+http://`** 或 **`sonnetdb+https://`**：协议前缀，声明远程连接模式
- **hostname:port**：SonnetDB 服务端的地址和端口
- **database**：要操作的数据库名称
- **token**：认证令牌，用于身份验证

以下是一个完整的 C# 代码示例：

```csharp
using SonnetDB.Data;

// 远程连接：通过 HTTP 协议访问远程 SonnetDB 服务
var connString = "sonnetdb+http://192.168.1.100:8080/mydb?token=sndb_abc123def456";
await using var conn = new SndbConnection(connString);
await conn.OpenAsync();

// 创建远程测量表
var cmd = conn.CreateCommand();
cmd.CommandText = @"
    CREATE MEASUREMENT machine_metrics (
        machine_id TAG,
        power FIELD DOUBLE,
        rpm FIELD DOUBLE,
        temperature FIELD DOUBLE
    )";
await cmd.ExecuteNonQueryAsync();
Console.WriteLine("远程测量表创建成功");
```

### Token 认证机制

SonnetDB 使用 Bearer Token 进行身份验证，确保只有授权客户端可以访问数据库。Token 的生成和管理通常在服务端配置文件中完成，您也可以在使用 `sndb` CLI 工具时通过 `sndb token create` 命令生成。

```csharp
// 使用 HTTPS 和 Token 的安全连接
var connString = "sonnetdb+https://sonnetdb.example.com:8443/production?token=sndb_xyz789";

await using var conn = new SndbConnection(connString);
await conn.OpenAsync();

// 验证连接状态
Console.WriteLine($"连接状态: {conn.State}");
Console.WriteLine($"服务器版本: {conn.ServerVersion}");
```

### 远程数据写入与查询

远程连接的编程模型与本地连接完全一致——`SndbCommand` 和 `SndbDataReader` 的 API 接口完全相同。区别在于，远程模式下 SQL 语句通过 HTTP 协议发送到服务端执行，结果以序列化形式返回。

```csharp
// 批量写入远程数据
cmd.CommandText = @"
    INSERT INTO machine_metrics (time, machine_id, power, rpm, temperature)
    VALUES (1713676800000, 'CNC-001', 12.5, 3200, 68.2),
           (1713676801000, 'CNC-001', 12.8, 3220, 68.5),
           (1713676802000, 'CNC-002', 8.3, 2800, 55.1)
";
var affected = await cmd.ExecuteNonQueryAsync();
Console.WriteLine($"已写入 {affected} 条记录");

// 远程查询
cmd.CommandText = @"
    SELECT time, power, rpm, temperature
    FROM machine_metrics
    WHERE machine_id = 'CNC-001'
      AND time >= 1713676800000
    ORDER BY time DESC
    LIMIT 50
";

await using var reader = await cmd.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    Console.WriteLine(
        $"机器: CNC-001, 功率: {reader.GetDouble(1)}kW, " +
        $"转速: {reader.GetInt32(2)}rpm, 温度: {reader.GetDouble(3)}°C"
    );
}
```

### 连接池与性能优化

远程模式下，SonnetDB ADO.NET 驱动内置了连接池机制，复用 HTTP 连接以减少握手开销。在处理高并发请求时，建议使用连接池并合理设置超时参数：

```csharp
// 配置连接池参数
var builder = new SndbConnectionStringBuilder
{
    DataSource = "sonnetdb+http://192.168.1.100:8080/mydb",
    Token = "sndb_abc123",
    ConnectTimeout = 5,      // 连接超时（秒）
    MaxPoolSize = 20         // 最大连接池大小
};

await using var conn = new SndbConnection(builder.ConnectionString);
await conn.OpenAsync();
```

总结来说，SonnetDB 通过 `sonnetdb+http://` 协议和标准的 ADO.NET 接口，实现了本地与远程编程体验的高度统一。无论您是构建边缘计算应用还是集中式时序数据平台，都可以使用同一套 API 完成开发工作，大幅降低了分布式时序系统的开发复杂度。
