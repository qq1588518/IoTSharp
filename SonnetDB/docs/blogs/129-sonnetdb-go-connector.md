## SonnetDB Go 连接器：cgo 与 database/sql 的双入口

Go 生态天然适合写边缘采集器、网关服务和轻量数据管道。SonnetDB Go 连接器的目标很直接：让 Go 程序可以在进程内打开 SonnetDB 数据目录，用 SQL 写入和查询时序数据，同时保留 Go 开发者熟悉的 `database/sql` 使用方式。

这第一版 Go 连接器基于 `connectors/c` 暴露的稳定 C ABI，通过 cgo 调用 `SonnetDB.Native.dll` / `SonnetDB.Native.so`。它不重新实现网络协议，也不解析 SonnetDB 文件格式，而是复用同一个 NativeAOT 引擎边界。

### 两层 API

Go 连接器提供两种入口：

1. `sonnetdb.Open` 这种轻量直接 API，适合嵌入式应用和工具程序。
2. `database/sql` driver，注册名为 `sonnetdb`，适合接入已有 Go 数据访问框架。

直接 API 大致如下：

```go
connection, err := sonnetdb.Open("./data-go")
if err != nil {
    return err
}
defer connection.Close()

_, err = connection.ExecuteNonQuery(
    "CREATE MEASUREMENT cpu (host TAG, usage FIELD FLOAT)")
if err != nil {
    return err
}

result, err := connection.Execute(
    "SELECT time, host, usage FROM cpu WHERE host = 'edge-1' LIMIT 10")
if err != nil {
    return err
}
defer result.Close()

for {
    ok, err := result.Next()
    if err != nil || !ok {
        break
    }

    ts, _ := result.Int64(0)
    host, _, _ := result.Text(1)
    usage, _ := result.Double(2)
    fmt.Println(ts, host, usage)
}
```

`database/sql` 入口则让调用方可以使用标准库抽象：

```go
import (
    "database/sql"

    _ "github.com/sonnetdb/sonnetdb/connectors/go"
)

db, err := sql.Open("sonnetdb", "./data-go")
```

### 值类型映射

C ABI 只暴露五类值：`NULL`、`INT64`、`DOUBLE`、`BOOL` 和 `TEXT`。Go 连接器把它们映射为自然 Go 类型：

| SonnetDB 类型 | Go typed getter | Go 值 |
| --- | --- | --- |
| `NULL` | `Value` | `nil` |
| `INT64` | `Int64` | `int64` |
| `DOUBLE` | `Double` | `float64` |
| `BOOL` | `Bool` | `bool` |
| `TEXT` | `Text` | `string` |

结果集是 forward-only cursor。每个 `Result` 需要调用 `Close`，连接也需要调用 `Close`。为了减少资源泄漏，连接器设置了 finalizer，但生产代码仍应显式关闭句柄。

### cgo 部署模型

Go 连接器需要 cgo，因此运行环境要满足三件事：

- Go 1.22+
- 可用的 C 编译器
- 当前平台对应的 `SonnetDB.Native` 共享库

Windows 示例：

```powershell
cmake -S connectors/c --preset windows-x64
cmake --build artifacts/connectors/c/win-x64 --config Release

cd connectors/go
$native = (Resolve-Path ../c/native/SonnetDB.Native/bin/Release/net10.0/win-x64/native).Path
$env:CGO_ENABLED = "1"
$env:CGO_LDFLAGS = "-L$native"
$env:PATH = "$native;$env:PATH"
go run ./examples/quickstart
```

Linux 示例：

```bash
cmake -S connectors/c --preset linux-x64
cmake --build artifacts/connectors/c/linux-x64

cd connectors/go
native="$(realpath ../../artifacts/connectors/c/linux-x64)"
CGO_ENABLED=1 CGO_LDFLAGS="-L$native" LD_LIBRARY_PATH="$native:${LD_LIBRARY_PATH:-}" \
  go run ./examples/quickstart
```

### 为什么暂不支持参数化 SQL

当前底层 C ABI 接受的是一条完整 SQL 字符串，还没有暴露 prepare/bind/step 风格接口。因此 Go 连接器第一版会在 `database/sql` 中明确拒绝参数数组。

这不是 Go 层的限制，而是 ABI 设计阶段的边界选择：先把 open、execute、cursor、typed getter、flush、last_error 做稳定，再在后续版本扩展参数绑定。这样可以保证 Go、Rust、Java、PureBasic 等语言在同一底层语义上迭代。

### 适用场景

Go 连接器最适合这些场景：

- 边缘网关直接在本地保存时序数据
- IoT collector 先落本地 SonnetDB，再批量上报
- 运维工具用 SQL 快速检查本地数据目录
- 已有 `database/sql` 生态希望接入 SonnetDB

Go 连接器让 SonnetDB 的嵌入式引擎进入 Go 程序，而不是要求 Go 程序额外部署一个数据库服务。对于追求简单部署的边缘和本地分析场景，这正是 SonnetDB 多语言连接器的价值所在。
