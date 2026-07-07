## CLI 高级技巧：配置文件管理、REPL 高效操作与跨平台使用

SonnetDB 的 `sndb` 命令行工具是日常管理和数据操作的核心入口。除了基本的数据库操作外，CLI 还提供了强大的配置文件管理、交互式 REPL 环境和跨平台支持。本文将深入介绍这些高级用法，帮助您充分发挥 CLI 的威力。

### 配置文件管理

`sndb` 支持通过 `sndb config` 命令管理多个配置文件，方便在不同环境之间切换。配置文件采用 TOML 格式，存储了连接地址、默认数据库、认证信息等参数。

```bash
# 设置当前 profile 的连接地址
sndb config set server.address "sonnetdb+http://192.168.1.100:8080"

# 设置默认数据库
sndb config set server.default_database "production"

# 设置认证 Token
sndb config set server.token "sndb_abc123def456"

# 查看当前配置
sndb config list

# 查看特定配置项
sndb config get server.address

# 使用指定配置文件启动
sndb --config ~/.sndb/production.toml
```

配置文件的层级结构支持全局配置（用户目录）和项目本地配置（当前目录下的 `.sndb/config.toml`），本地配置优先级更高。这种设计让团队可以在项目中共享连接配置，同时保留个人偏好的独立设置。

### REPL 高效操作技巧

`sndb repl` 命令启动交互式命令行环境，支持智能提示、历史记录和多行输入等特性，是数据探索和分析的利器。

```bash
# 启动 REPL（使用默认配置）
sndb repl

# 启动 REPL 并指定数据库
sndb repl --database testdb
```

进入 REPL 后，以下技巧能大幅提升操作效率：

```sql
-- 基础操作：列出所有测量表
> SHOW MEASUREMENTS;

-- 描述表结构
> DESCRIBE machine_metrics;

-- 使用 \ 命令控制 REPL
> \timing on      -- 开启执行时间显示
> \echo on        -- 开启回显
> \multiline on   -- 开启多行模式，支持复杂 SQL

-- 多行模式示例（开启后支持跨行输入）
> SELECT time, temperature, humidity
FROM sensor_data
WHERE device_id = 'sensor-01'
  AND time >= 1713676800000
ORDER BY time DESC
LIMIT 10;

-- 查看历史命令
> \history

-- 清除屏幕
> \clear
```

### 跨平台使用

`sndb` CLI 使用 .NET 10 构建，原生支持 Windows、Linux 和 macOS 三大平台。安装方式多样：

```bash
# Windows (使用 dotnet tool)
dotnet tool install --global SonnetDB.CLI

# Linux (使用 DEB 包)
sudo dpkg -i sndb-cli_0.6.0_amd64.deb

# macOS (使用 Homebrew)
brew tap sonnetdb/tap
brew install sndb

# 验证安装
sndb --version
sndb --help
```

### 脚本化执行

除了交互模式外，`sndb` 支持直接从文件执行 SQL 脚本，适合自动化任务和 CI/CD 集成：

```bash
# 执行 SQL 脚本文件
sndb execute --file scripts/migration.sql

# 管道模式：从标准输入读取 SQL
echo "SELECT count(*) FROM sensor_data" | sndb execute

# 格式化输出为 JSON
sndb execute --file query.sql --format json

# 输出为 CSV（适合导入电子表格）
sndb execute --file query.sql --format csv --output result.csv
```

### 性能诊断命令

CLI 还内置了性能诊断工具，帮助您快速定位问题：

```bash
# 查看数据库统计信息
sndb stat

# 列出所有数据段及其大小
sndb segments list

# 手动触发合并操作
sndb segments compact

# 查看慢查询日志
sndb query --slow --limit 10
```

总结而言，SonnetDB 的 `sndb` CLI 不仅是一个简单的数据库客户端，更是一个功能完备的管理工具集。通过灵活的配置管理、高效的 REPL 环境和跨平台支持，无论您是开发者、DBA 还是运维工程师，都能在工作中得心应手。
