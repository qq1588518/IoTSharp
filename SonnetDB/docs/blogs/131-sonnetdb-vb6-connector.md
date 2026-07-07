## SonnetDB Visual Basic 6 连接器：让经典 Windows 应用接入时序数据

Visual Basic 6 已经是很古老的技术栈，但在工业现场、设备配置工具、产线看板和长期维护的 Windows 桌面系统里，它仍然真实存在。SonnetDB VB6 连接器的目标不是把 VB6 变成现代平台，而是给这些存量系统一条可控的嵌入式时序数据接入路径。

VB6 连接器第一版包含三部分：

- 一个 x86 stdcall 桥接 DLL 源码
- `.bas` / `.cls` VB6 源模块
- 一个 quickstart `.vbp` 示例工程

### 为什么需要 bridge

SonnetDB 的底层 C ABI 使用 cdecl 调用约定，这是 C、Go、Rust、Java FFM 等生态最自然的选择。但 VB6 的 `Declare` 语句调用 DLL 时使用 stdcall，并且 VB6 是 32-bit Windows runtime。

所以 VB6 不能直接可靠调用 `SonnetDB.Native.dll` 的 cdecl 导出函数。连接器加入了一个很小的桥接层：

```
VB6 应用
  │ Declare stdcall
  ▼
SonnetDB.VB6.Native.dll  （x86 stdcall bridge）
  │ LoadLibrary/GetProcAddress
  ▼
SonnetDB.Native.dll      （x86 cdecl C ABI）
```

桥接层还负责 UTF-16 与 UTF-8 字符串转换。VB6 侧使用 Unicode `String`，SonnetDB C ABI 使用 UTF-8 `char*`。

### VB6 API

VB6 侧提供两个主要类：

- `SonnetDbConnection`
- `SonnetDbResult`

示例：

```vb
Dim connection As SonnetDbConnection
Dim result As SonnetDbResult

Set connection = New SonnetDbConnection
connection.Open App.Path & "\data-vb6"

connection.ExecuteNonQuery "CREATE MEASUREMENT cpu (host TAG, usage FIELD FLOAT)"
connection.ExecuteNonQuery _
    "INSERT INTO cpu (time, host, usage) VALUES " & _
    "(1710000000000, 'edge-1', 0.42)"

Set result = connection.Execute("SELECT time, host, usage FROM cpu LIMIT 10")

Do While result.NextRow
    Debug.Print result.GetInt64Text(0), result.GetString(1), result.GetDouble(2)
Loop

result.Close
connection.Close
```

`GetInt64` 在 VB6 里返回 `Double`，这是为了兼容 VB6 原生数值能力；如果要保留完整 64-bit 时间戳文本表示，应使用 `GetInt64Text`。

### 构建方式

因为 VB6 是 32-bit runtime，必须构建 x86 版本：

```powershell
cmake -S connectors/c --preset windows-x86
cmake --build artifacts/connectors/c/win-x86 --config Release

cmake -S connectors/vb6 --preset windows-x86
cmake --build artifacts/connectors/vb6/win-x86 --config Release
```

运行时把以下文件放在 VB6 exe 旁边，或加入 `PATH`：

- `SonnetDB.Native.dll`
- `SonnetDB.VB6.Native.dll`

然后在 VB6 工程中加入：

- `src/SonnetDbNative.bas`
- `src/SonnetDbConnection.cls`
- `src/SonnetDbResult.cls`

### CI 策略

VB6 连接器不在 GitHub Actions hosted runner 中构建 VB6 工程，也不构建 VB6 产出的动态库。原因很现实：

- GitHub-hosted runner 不包含授权的 VB6 IDE/compiler
- VB6 是经典 32-bit Windows 工具链，环境安装和授权更适合本地机器或 self-hosted runner
- SonnetDB 仓库只提供源模块和 bridge 源码，避免把不可复现的商业工具链塞进公共 CI

桥接 DLL 是普通 C 源码，可以在有 Win32 C++ toolchain 的本地机器构建。VB6 工程本身由用户自己的授权环境编译。

### 适用场景

VB6 连接器适合这些存量系统：

- 设备配置工具需要记录采样点
- 工厂看板需要本地缓存指标
- 老旧 HMI / SCADA 周边工具需要 SQL 查询窗口
- 不能引入数据库服务，但可以随应用携带 DLL 的桌面程序

对这些系统来说，最重要的不是追求最新语言特性，而是低侵入、可部署、可回滚。VB6 连接器的价值正在这里：用一个很小的 bridge，把 SonnetDB 的现代嵌入式时序能力接到经典 Windows 应用里。
