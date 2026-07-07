## SonnetDB Java 连接器：一套 API，双后端驱动

Java 生态是 SonnetDB 多语言支持战略中的重要一环。我们推出的 Java 连接器在提供简洁公共 API 的同时，设计了一套独特的**双后端架构**——底层自动适配 Java 8 的 JNI 和 Java 21 的 Foreign Function & Memory API（FFM，即 Panama 项目）。同一个 JAR 包，在不同 JDK 版本上自动选择最优路径。

### 架构全景

```
┌──────────────────────────────────────────────────┐
│              你的 Java 应用程序                    │
│  SonnetDbConnection / SonnetDbResult（公共 API）   │
└─────────────────────┬────────────────────────────┘
                      │
              NativeBackend（SPI 接口）
                      │
        ┌─────────────┴─────────────┐
        │                           │
  ┌─────▼──────┐           ┌───────▼────────┐
  │ JNI Backend│           │  FFM Backend    │
  │ (Java 8+)  │           │  (Java 21+)     │
  └─────┬──────┘           └───────┬────────┘
        │                          │
  ┌─────▼──────┐           ┌───────▼────────┐
  │JNI Bridge  │           │  直接调用        │
  │.dll/.so    │           │  MethodHandle   │
  └─────┬──────┘           └───────┬────────┘
        │                          │
        └──────────┬───────────────┘
                   │
        ┌──────────▼──────────┐
        │ SonnetDB.Native     │
        │ （核心数据库引擎）     │
        └─────────────────────┘
```

关键在于 `NativeBackend` 接口——它定义了 14 个方法，将公共 API 与原生调用细节完全解耦。`SonnetDbConnection` 和 `SonnetDbResult` 不包含任何 JNI 或 FFM 相关的代码，只通过后端接口与原生库交互。

### 公共 API：简洁即美

Java 连接器仅公开 4 个类和 1 个枚举，学习曲线极低：

```java
// 打开嵌入式数据库
try (SonnetDbConnection conn = SonnetDbConnection.open("./mydb")) {

    // 建表
    conn.executeNonQuery(
        "CREATE MEASUREMENT cpu (host TAG, usage FIELD FLOAT)");

    // 写入（返回影响行数）
    int inserted = conn.executeNonQuery(
        "INSERT INTO cpu (time, host, usage) VALUES " +
        "(1710000000000, 'edge-1', 0.42), " +
        "(1710000001000, 'edge-1', 0.73)");

    // 查询（try-with-resources 自动释放）
    try (SonnetDbResult rs = conn.execute(
            "SELECT time, host, usage FROM cpu " +
            "WHERE host = 'edge-1' ORDER BY time DESC LIMIT 10")) {

        while (rs.next()) {
            long time = rs.getLong(0);
            String host = rs.getString(1);
            double usage = rs.getDouble(2);
            System.out.printf("%d\t%s\t%.3f%n", time, host, usage);
        }
    }
}
```

API 设计遵循三个原则：**try-with-resources 自动资源管理**、**0 为基数的列序号**、**类型安全的 getter 方法**。`executeNonQuery()` 是针对 INSERT/DELETE/DDL 的便捷方法，内部自动释放结果集。

### 双后端深度对比

这是 Java 连接器最具特色的设计。同一个 API，底层可以搭载两套完全不同的原生调用机制。

| 维度 | JNI 后端（Java 8+） | FFM 后端（Java 21+） |
|------|-------------------|---------------------|
| 原生库数量 | 2 个（核心库 + JNI 桥接） | 1 个（仅核心库） |
| 符号绑定 | `GetProcAddress`/`dlsym` 动态解析 | `SymbolLookup` + `MethodHandle` |
| 内存管理 | JNI 引用 + `ReleaseStringUTFChars` | `Arena.ofConfined()` 作用域 |
| 额外 JVM 参数 | 无 | `--enable-preview --enable-native-access=ALL-UNNAMED` |
| 原生编译依赖 | 需为每平台编译 `sonnetdb_jni.c` | 无额外原生编译 |

**JNI 后端的加载链**：JNI 桥接库（`SonnetDB.Java.Native.dll`）在初始化时通过 `LoadLibrary`/`dlopen` 动态加载核心库 `SonnetDB.Native`，并通过 `GetProcAddress`/`dlsym` 解析全部 16 个 C ABI 符号的函数指针。每个 JNI 方法调用通过函数指针转发到核心库。

**FFM 后端的加载链**：Java 侧直接通过 `System.load()` 加载核心库，使用 `SymbolLookup.loaderLookup()` 查找符号，通过 `Linker.nativeLinker().downcallHandle()` 创建 `MethodHandle`。每次调用时使用 `Arena.ofConfined()` 创建临时内存作用域，调用结束后自动释放。整个过程不涉及任何 C 代码编译。

### 多版本 JAR：一个构件，版本自适应

Java 连接器利用 JEP 238 的多版本 JAR 机制，实现了同一个 JAR 在 Java 8 和 Java 21 上的无缝运行：

```
sonnetdb-java.jar
├── com/sonnetdb/
│   ├── SonnetDbConnection.class          ← Java 8 编译的公共 API
│   ├── SonnetDbResult.class
│   ├── jni/
│   │   ├── SonnetDbJni.class             ← JNI 原生方法声明
│   │   └── SonnetDbJniBackend.class      ← JNI 后端实现
│   └── ffm/
│       └── SonnetDbFfmBackend.class      ← 占位实现（Java 8 上直接抛异常）
└── META-INF/
    ├── MANIFEST.MF  (Multi-Release: true)
    └── versions/21/
        └── com/sonnetdb/ffm/
            └── SonnetDbFfmBackend.class  ← 真正的 FFM 实现（Java 21 上覆盖基类）
```

- 在 **JDK < 21** 上，`META-INF/versions/21/` 目录被忽略，FFM 后端类为占位实现
- 在 **JDK 21+** 上，版本化目录中的类自动覆盖基类，获得真正的 FFM 后端
- 无需维护两个 JAR，无需在代码中使用反射判断版本

### 后端选择：显式指定还是自动检测

后端选择通过系统属性 `sonnetdb.java.backend` 或环境变量 `SONNETDB_JAVA_BACKEND` 控制，支持三种模式：

- **`jni`**（默认）—— 始终使用 JNI 后端，兼容 Java 8+
- **`ffm`** —— 使用 FFM 后端，需要 JDK 21+
- **`auto`** —— JDK 版本 >= 21 时优先尝试 FFM，失败则回退 JNI；低于 21 则直接使用 JNI

```powershell
# JNI 后端（默认，Java 8+）
java -Dsonnetdb.native.path=./SonnetDB.Native.dll \
     -Dsonnetdb.jni.path=./SonnetDB.Java.Native.dll \
     -cp sonnetdb-java.jar:myapp.jar \
     com.example.Quickstart

# FFM 后端（JDK 21+）
java --enable-preview --enable-native-access=ALL-UNNAMED \
     -Dsonnetdb.java.backend=ffm \
     -Dsonnetdb.native.path=./SonnetDB.Native.dll \
     -cp sonnetdb-java.jar:myapp.jar \
     com.example.Quickstart
```

### 如何选择后端？

**选择 JNI 后端**，如果你需要在 Java 8 / 11 / 17 等 LTS 版本上运行，或者不想在启动命令中添加 `--enable-preview` 等实验性 JVM 参数。

**选择 FFM 后端**，如果你的运行环境已经是 JDK 21+，希望减少部署的原生库数量（不需要 JNI 桥接库），或者对 Panama 项目的零拷贝内存模型感兴趣。

无论选择哪种后端，公共 API 的行为完全一致——`NativeBackend` 接口确保了这一点。同一份业务代码可以在两种后端之间自由切换，无需任何修改。

### 小结

SonnetDB Java 连接器通过 SPI 接口解耦和多版本 JAR 机制，实现了 Java 8 到 Java 21 的全版本覆盖。公共 API 简洁直观，双后端架构在兼容性和性能之间取得了平衡。对于在 JVM 生态中构建 IoT 平台、数据处理服务和嵌入式应用的开发者来说，Java 连接器提供了与 SonnetDB 最自然的集成方式。
