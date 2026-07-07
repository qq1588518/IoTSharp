## SonnetDB PureBasic 连接器：用 Include 文件动态加载 Native 引擎

PureBasic 的特点是语法直接、编译产物小、跨平台能力强，常用于桌面工具、自动化程序和一些轻量系统工具。SonnetDB PureBasic 连接器选择了最朴素的实现方式：一个 `.pbi` include 文件，运行时动态加载 `SonnetDB.Native`，然后通过函数指针调用 C ABI。

没有额外 wrapper DLL，也没有复杂构建系统。PureBasic 程序只要能找到 `SonnetDB.Native.dll` 或 `SonnetDB.Native.so`，就可以打开数据库目录并执行 SQL。

### 架构

```
PureBasic 应用
  │ XIncludeFile "SonnetDB.pbi"
  ▼
OpenLibrary / GetFunction
  │
  ▼
SonnetDB.Native.dll / SonnetDB.Native.so
```

`SonnetDB.pbi` 中定义了 C ABI 函数的 `PrototypeC`，包括：

- `sonnetdb_open`
- `sonnetdb_execute`
- `sonnetdb_result_next`
- typed getter
- `sonnetdb_flush`
- `sonnetdb_version`
- `sonnetdb_last_error`

PureBasic 侧字符串转换为 UTF-8 后传入 native 层，返回的 UTF-8 指针通过 `PeekS(..., #PB_UTF8)` 转为 PureBasic 字符串。

### 使用示例

```purebasic
XIncludeFile "src/SonnetDB.pbi"

If SonnetDB_Load()
  *connection = SonnetDB_Open("./data-purebasic")

  SonnetDB_ExecuteNonQuery(*connection,
    "CREATE MEASUREMENT cpu (host TAG, usage FIELD FLOAT)")

  SonnetDB_ExecuteNonQuery(*connection,
    "INSERT INTO cpu (time, host, usage) VALUES " +
    "(1710000000000, 'edge-1', 0.42)")

  *result = SonnetDB_Execute(*connection,
    "SELECT time, host, usage FROM cpu LIMIT 10")

  While SonnetDB_ResultNext(*result) = 1
    Debug Str(SonnetDB_ResultValueInt64(*result, 0)) + " " +
      SonnetDB_ResultValueText(*result, 1) + " " +
      StrD(SonnetDB_ResultValueDouble(*result, 2), 3)
  Wend

  SonnetDB_ResultFree(*result)
  SonnetDB_Close(*connection)
EndIf
```

### 值类型与过程命名

PureBasic 连接器保留了接近 C ABI 的过程命名：

| 过程 | 作用 |
| --- | --- |
| `SonnetDB_Open` | 打开数据库目录 |
| `SonnetDB_Execute` | 执行 SQL 并返回 result handle |
| `SonnetDB_ExecuteNonQuery` | 执行非查询 SQL 并返回影响行数 |
| `SonnetDB_ResultNext` | 移动到下一行 |
| `SonnetDB_ResultValueInt64` | 读取 `INT64` |
| `SonnetDB_ResultValueDouble` | 读取 `DOUBLE` |
| `SonnetDB_ResultValueBool` | 读取 `BOOL` |
| `SonnetDB_ResultValueText` | 读取 `TEXT` |

这种风格更适合 PureBasic：少做对象包装，保持过程调用清楚可见。

### 构建与运行

Windows：

```powershell
cmake -S connectors/c --preset windows-x64
cmake --build artifacts/connectors/c/win-x64 --config Release

cd connectors/purebasic
$native = (Resolve-Path ../../artifacts/connectors/c/win-x64/Release).Path
$env:PATH = "$native;$env:PATH"
pbcompiler examples\quickstart.pb --console --output quickstart.exe
.\quickstart.exe
```

Linux：

```bash
cmake -S connectors/c --preset linux-x64
cmake --build artifacts/connectors/c/linux-x64

cd connectors/purebasic
native="$(realpath ../../artifacts/connectors/c/linux-x64)"
pbcompiler examples/quickstart.pb --console --output quickstart
LD_LIBRARY_PATH="$native:${LD_LIBRARY_PATH:-}" ./quickstart
```

### CI 策略

PureBasic 有命令行编译器，可以用于本地或 self-hosted runner 自动构建。但它不是 GitHub-hosted runner 预装工具，也没有 SonnetDB 仓库可以直接使用的官方 setup action 来配置授权编译器。

因此 SonnetDB 不在公共 CI 中构建 PureBasic 可执行文件或 PureBasic 产出的动态库。仓库提供 `.pbi` 源码和 quickstart；需要二进制产物的团队可以在自己的授权环境里构建。

### 适用场景

PureBasic 连接器适合：

- 小型 Windows/Linux 桌面工具
- 现场调试程序
- 轻量数据采集器
- 需要携带一个本地时序库的自动化脚本型应用

它没有复杂框架，也没有服务端要求。一个 include 文件加一个 NativeAOT 共享库，就能让 PureBasic 程序使用 SonnetDB 的 SQL、WAL 和查询引擎。
