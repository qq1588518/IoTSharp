## SonnetDB C 连接器：嵌入式时序数据库的原生接入

SonnetDB 从设计之初就定位为**嵌入式优先**的时序数据库。为了让 C/C++ 生态能够直接嵌入 SonnetDB 引擎，我们推出了全新的 C 连接器——一套最小化的 C ABI，仅包含 **17 个函数**和一个头文件，让任何支持 C FFI 的语言都能无缝接入。

### 架构设计：当 .NET 遇上 C

C 连接器的架构颇为独特：**库的实现是 C#，但对外暴露的是纯 C 接口**。整个流程如下：

```
┌─────────────────────────┐
│  你的 C/C++ 应用程序      │
│  #include "sonnetdb.h"   │
└───────────┬─────────────┘
            │ C ABI（17 个导出函数，cdecl 调用约定）
┌───────────▼─────────────┐
│  SonnetDB.Native.dll/so  │
│  （.NET NativeAOT 编译）   │
│  内含完整 SonnetDB 引擎    │
└──────────────────────────┘
```

核心库由 .NET NativeAOT 技术编译为本机共享库（`SonnetDB.Native.dll` 或 `SonnetDB.Native.so`），所有导出函数通过 `[UnmanagedCallersOnly]` 特性以 cdecl 调用约定暴露。这意味着 C 代码只需要链接这一个库文件，无需依赖 .NET 运行时——NativeAOT 已经将整个引擎（包括 GC）编译为独立的本机代码。

### 17 个函数，覆盖完整生命周期

C 连接器的 API 刻意保持最小化。所有类型通过不透明句柄传递，不暴露任何内部结构：

**连接管理：**
```c
sonnetdb_connection* sonnetdb_open(const char* data_source);
void                 sonnetdb_close(sonnetdb_connection* connection);
int32_t              sonnetdb_flush(sonnetdb_connection* connection);
```

**SQL 执行与结果遍历：**
```c
sonnetdb_result*     sonnetdb_execute(sonnetdb_connection* connection, const char* sql);
void                 sonnetdb_result_free(sonnetdb_result* result);
int32_t              sonnetdb_result_records_affected(sonnetdb_result* result);
int32_t              sonnetdb_result_column_count(sonnetdb_result* result);
const char*          sonnetdb_result_column_name(sonnetdb_result* result, int32_t ordinal);
int32_t              sonnetdb_result_next(sonnetdb_result* result);
sonnetdb_value_type  sonnetdb_result_value_type(sonnetdb_result* result, int32_t ordinal);
int64_t              sonnetdb_result_value_int64(sonnetdb_result* result, int32_t ordinal);
double               sonnetdb_result_value_double(sonnetdb_result* result, int32_t ordinal);
int32_t              sonnetdb_result_value_bool(sonnetdb_result* result, int32_t ordinal);
const char*          sonnetdb_result_value_text(sonnetdb_result* result, int32_t ordinal);
```

**工具函数：**
```c
int32_t sonnetdb_version(char* buffer, int32_t buffer_length);
int32_t sonnetdb_last_error(char* buffer, int32_t buffer_length);
```

注意错误处理的设计模式：`sonnetdb_last_error` 和 `sonnetdb_version` 都采用"调用者提供缓冲区"的方式，返回所需的字节数。错误信息是线程局部的，成功调用会自动清除上一个错误。

### 快速上手示例

以下是一个完整的 C 程序，演示了从打开数据库到查询结果的完整流程：

```c
#include <stdio.h>
#include "sonnetdb.h"

int main() {
    // 打开嵌入式数据库（参数为目录路径）
    sonnetdb_connection* conn = sonnetdb_open("./mydb");
    if (!conn) {
        char err[1024];
        sonnetdb_last_error(err, sizeof(err));
        fprintf(stderr, "打开失败: %s\n", err);
        return 1;
    }

    // 创建测量
    sonnetdb_result* r = sonnetdb_execute(conn,
        "CREATE MEASUREMENT cpu (host TAG, usage FIELD FLOAT)");
    sonnetdb_result_free(r);

    // 写入数据
    r = sonnetdb_execute(conn,
        "INSERT INTO cpu (time, host, usage) VALUES "
        "(1710000000000, 'edge-1', 0.42), "
        "(1710000001000, 'edge-1', 0.73)");
    printf("插入了 %d 行\n", sonnetdb_result_records_affected(r));
    sonnetdb_result_free(r);

    // 查询并遍历结果
    r = sonnetdb_execute(conn,
        "SELECT time, host, usage FROM cpu WHERE host = 'edge-1' LIMIT 10");

    int cols = sonnetdb_result_column_count(r);
    for (int i = 0; i < cols; i++)
        printf("%s\t", sonnetdb_result_column_name(r, i));
    printf("\n");

    while (sonnetdb_result_next(r) == 1) {
        printf("%lld\t%s\t%.3f\n",
            (long long)sonnetdb_result_value_int64(r, 0),
            sonnetdb_result_value_text(r, 1),
            sonnetdb_result_value_double(r, 2));
    }
    sonnetdb_result_free(r);

    sonnetdb_flush(conn);
    sonnetdb_close(conn);
    return 0;
}
```

### 值类型系统

SonnetDB 定义了五种值类型，覆盖了时序数据中的常见需求：

| 类型 | 枚举值 | 说明 |
|------|--------|------|
| `SONNETDB_TYPE_NULL` | 0 | 空值 |
| `SONNETDB_TYPE_INT64` | 1 | 64 位有符号整数 |
| `SONNETDB_TYPE_DOUBLE` | 2 | 双精度浮点数 |
| `SONNETDB_TYPE_BOOL` | 3 | 布尔值 |
| `SONNETDB_TYPE_TEXT` | 4 | UTF-8 文本 |

所有数值类型均可通过 `value_int64` 或 `value_double` 读取，引擎会自动进行类型转换。`GeoPoint` 地理坐标会自动格式化为 `POINT(lat,lon)` 的 WKT 表示，`float[]` 向量则格式化为 `[v0,v1,...]`。

### 构建与平台支持

C 连接器使用 CMake 构建，需要 .NET 10.0 SDK（用于 NativeAOT 编译）。支持以下平台：

| 平台 | 架构 |
|------|------|
| Windows | x64, x86, ARM64 |
| Linux | x64 |

```powershell
# Windows (x64)
cmake -S connectors/c --preset windows-x64
cmake --build artifacts/connectors/c/win-x64 --config Release

# Linux (x64)
cmake -S connectors/c -B artifacts/connectors/c/linux-x64 \
  -DSONNETDB_C_RID=linux-x64 -DCMAKE_BUILD_TYPE=Release
cmake --build artifacts/connectors/c/linux-x64
```

### C ABI 作为跨语言基石

C 连接器的意义不止于 C/C++ 生态。作为 SonnetDB 最底层的跨语言桥梁，这套 C ABI 也是 Java、Rust、Go、Python 等语言连接器的共同基础。所有语言的连接器最终都消费同一个 `SonnetDB.Native` 共享库，确保了跨语言行为的一致性和维护的高效性。

对于嵌入式系统、IoT 网关、边缘计算等资源受限且以 C/C++ 为主要开发语言的场景，C 连接器提供了最轻量、最原生的 SonnetDB 接入方式。
