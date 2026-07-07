## SonnetDB 行协议写入：兼容 InfluxDB Line Protocol 的大批量数据接入

SonnetDB 原生支持 InfluxDB Line Protocol（行协议）格式，这是时序数据生态中最广泛使用的文本序列化格式。通过兼容该协议，SonnetDB 可以无缝对接 Telegraf、Prometheus remote write 以及各类 IoT 设备的数据上报，实现零改造的数据接入。

### 行协议格式

Line Protocol 每行代表一个数据点，语法如下：

```
<measurement>[,<tag>=<value>...] <field>=<value>[,<field>=<value>...] [<timestamp>]
```

时间戳默认为纳秒精度，SonnetDB 同时支持纳秒、微秒和毫秒三种精度：

```text
sensor,host=edge-01,type=temperature value=23.5 1713676800000000000
sensor,host=edge-01,type=humidity  value=65.2 1713676801000000000
sensor,host=edge-02,type=temperature value=21.8 1713676802000000000
```

### HTTP 批量写入端点

SonnetDB 服务端提供 `/v1/db/{db}/measurements/{m}/lp` 端点，开箱即用：

```bash
curl -X POST "http://127.0.0.1:5080/v1/db/metrics/measurements/sensor/lp?flush=async" \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: text/plain" \
  --data-binary @data.lp
```

`flush` 参数支持三档模式：`false`（默认，仅入 MemTable + WAL，最快）、`async`（发后台 flush 信号后立即返回）、`true|sync`（同步等待落盘）。高吞吐场景推荐使用 `flush=false` 或 `flush=async`。

### ADO.NET TableDirect 行协议

嵌入式和远程模式均可通过 ADO.NET `TableDirect` 直接写入行协议字符串：

```csharp
using System.Data;
using SonnetDB.Data;

using var connection = new SndbConnection("Data Source=./demo-data");
connection.Open();

using var command = connection.CreateCommand();
command.CommandType = CommandType.TableDirect;
command.CommandText = """
sensor,host=edge-01,type=temperature value=23.5 1713676800000000000
sensor,host=edge-01,type=humidity value=65.2 1713676801000000000
""";
command.Parameters.AddWithValue("measurement", "sensor");
command.Parameters.AddWithValue("flush", "async");

var written = command.ExecuteNonQuery();
```

### 自动 Schema 推断

行协议的一大优势是 Schema-less 写入。当 SonnetDB 接收到行协议数据时，如果对应的 Measurement 尚不存在，会自动推断并创建 Schema；如果 Measurement 已存在但缺少新的 tag / field，也会在写入前自动补齐。Tag 列自动识别为字符串标签，Field 列根据写入值推断为 `FLOAT`、`INT`、`BOOL` 或 `STRING`；已有 `INT` 字段遇到 `FLOAT` 值时会提升为 `FLOAT`：

```text
# 首次写入自动建表
weather,station=beijing temperature=28.5,wind_speed=3.2 1713676800000000000
```

### Telegraf 集成

SonnetDB 兼容 InfluxDB 输出插件，Telegraf 直接配置即可：

```toml
[[outputs.influxdb]]
  urls = ["http://127.0.0.1:5080"]
  database = "telegraf"
  skip_database_creation = false
  content_encoding = "gzip"
```

### 性能表现

在批量行协议写入基准测试中，嵌入式模式单次写入 100 万点仅需约 545 ms，吞吐量达 **1.83 M pts/s**；服务端 LP 端点 100 万点约 1.20 秒、内存分配仅 52 MB，比 SQL INSERT 快 15 倍以上。行协议以其简洁性和高解析效率，成为 SonnetDB 推荐的大批量数据接入格式。
