## SonnetDB Rust 连接器：手写 FFI 与安全封装

Rust 用户通常关心两件事：边界是否清楚，资源释放是否可靠。SonnetDB Rust 连接器围绕这两点设计：底层手写 FFI 对齐 `connectors/c/include/sonnetdb.h`，上层提供 `Connection` 和 `ResultSet` 两个安全封装类型。

它不依赖 bindgen，也不引入额外 crate。第一版保持很小：一个 `Cargo.toml`、一个 `build.rs`、一份 FFI 声明和一个 safe wrapper。

### 架构

Rust 连接器的调用链是：

```
Rust 应用
  │
  ├─ sonnetdb::Connection
  ├─ sonnetdb::ResultSet
  │
  ▼
手写 extern "C" FFI
  │
  ▼
SonnetDB.Native.dll / SonnetDB.Native.so
```

`build.rs` 负责根据平台链接 `SonnetDB.Native`：

- Windows：链接 `SonnetDB.Native.lib`，运行时加载 `SonnetDB.Native.dll`
- Linux：链接 `SonnetDB.Native.so`

如果默认路径找不到原生库，可以通过 `SONNETDB_NATIVE_LIB_DIR` 指定。

### 安全 API

Rust 侧公开的核心 API 很短：

```rust
let connection = sonnetdb::Connection::open("./data-rust")?;
connection.execute_non_query(
    "CREATE MEASUREMENT cpu (host TAG, usage FIELD FLOAT)")?;

let mut result = connection.execute(
    "SELECT time, host, usage FROM cpu WHERE host = 'edge-1' LIMIT 10")?;

while result.next()? {
    let ts = result.get_i64(0)?;
    let host = result.get_text(1)?.unwrap_or_default();
    let usage = result.get_f64(2)?;
    println!("{ts}\t{host}\t{usage:.3}");
}
```

`Connection` 和 `ResultSet` 都实现了 `Drop`，即使调用方忘记显式 `close`，native handle 也会被释放。显式 `close` 仍然存在，方便在需要捕获关闭错误时使用。

### 值类型

Rust 连接器定义了两个枚举：

```rust
pub enum ValueType {
    Null,
    Int64,
    Double,
    Bool,
    Text,
}

pub enum Value {
    Null,
    Int64(i64),
    Double(f64),
    Bool(bool),
    Text(String),
}
```

typed getter 会先检查当前列的 `ValueType`，避免把文本当作数值读取。`get_f64` 允许 `Int64`，因为底层 C ABI 本身支持整数到 double 的读取转换。

### 字符串与错误处理

C ABI 中字符串全部是 UTF-8。Rust 连接器在边界处使用 `CString` 传入 SQL 和路径，返回值通过 `CStr` 转为 `String`。

错误处理也跟随 C ABI 的模式：native 调用失败后读取 `sonnetdb_last_error`，包装为 Rust 的 `Error` 类型。这样调用方可以统一使用 `Result<T, Error>`。

### 构建示例

Windows：

```powershell
cmake -S connectors/c --preset windows-x64
cmake --build artifacts/connectors/c/win-x64 --config Release

cd connectors/rust
$native = (Resolve-Path ../c/native/SonnetDB.Native/bin/Release/net10.0/win-x64/native).Path
$env:SONNETDB_NATIVE_LIB_DIR = $native
$env:PATH = "$native;$env:PATH"
cargo run --example quickstart
```

Linux：

```bash
cmake -S connectors/c --preset linux-x64
cmake --build artifacts/connectors/c/linux-x64

cd connectors/rust
native="$(realpath ../../artifacts/connectors/c/linux-x64)"
SONNETDB_NATIVE_LIB_DIR="$native" LD_LIBRARY_PATH="$native:${LD_LIBRARY_PATH:-}" \
  cargo run --example quickstart
```

### 为什么手写 FFI

SonnetDB 的 C ABI 很小，只有 opaque handle、primitive 值和 UTF-8 字符串。手写 FFI 有几个好处：

- 不需要引入 bindgen 和 libclang 依赖
- public wrapper 可以保持稳定，不受 C header 生成细节影响
- 更容易审查每个 unsafe 调用点
- 符合 SonnetDB 连接器“最小边界、语言自然封装”的原则

未来如果 C ABI 扩展到参数绑定或批量写入接口，Rust 层可以继续在小范围内手写补齐，而不是把复杂度扩散到构建系统。

### 适用场景

Rust 连接器适合本地 agent、边缘分析服务、工业网关和需要严谨资源管理的嵌入式工具。它保留 Rust 的所有权模型，同时复用 SonnetDB.Native 中已经实现的 SQL、WAL、Segment 和查询引擎。

这也是 SonnetDB 多语言策略的一条主线：底层引擎只维护一份，语言连接器负责把它翻译成各自生态中自然的使用方式。
