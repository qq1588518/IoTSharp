## CLI 工具安装与使用：sndb 命令行指南

SonnetDB 提供了功能完备的命令行工具 `sndb`，它可以帮助您在不打开浏览器的情况下完成数据库管理任务。`sndb` 以 .NET 全局工具的形式发布，安装和使用都非常便捷。本文将详细介绍其安装过程和常用操作。

### 安装 .NET 全局工具

`sndb` CLI 工具通过 NuGet 包管理器分发，安装前请确保您的系统已经安装了 .NET 10 SDK。如果尚未安装，请从 [dotnet.microsoft.com](https://dotnet.microsoft.com) 下载。

安装命令非常简单：

```bash
dotnet tool install --global SonnetDB.CLI
```

这条命令会从 NuGet.org 下载最新版本的 `SonnetDB.CLI` 包，并将其注册为全局工具。安装完成后，您可以在终端中直接使用 `sndb` 命令。执行以下命令验证安装是否成功：

```bash
sndb --version
```

如果显示版本号（例如 `0.6.0`），说明安装成功。如果您需要更新到最新版本，可以使用：

```bash
dotnet tool update --global SonnetDB.CLI
```

### 配置文件管理

`sndb` 使用配置文件来管理多个数据库连接。配置文件的默认位置是 `~/.sonnetdb/profiles.json`。您可以使用 `sndb config` 系列命令来管理连接配置：

```bash
# 添加一个新的连接配置
sndb config add my-server --host 192.168.1.100 --port 8839 --token your-token-here

# 列出所有配置
sndb config list

# 切换到指定配置
sndb config use my-server

# 查看当前配置详情
sndb config show
```

多配置文件管理在开发环境中非常实用。例如，您可以分别配置开发环境、测试环境和生产环境的连接信息，通过 `sndb config use` 命令快速切换。

### 常用命令速览

`sndb` 提供了一系列实用的数据库管理命令：

```bash
# 检查服务器健康状态
sndb health

# 列出所有测量（measurement）
sndb list measurements

# 执行 SQL 查询（交互模式）
sndb sql

# 执行单条 SQL 命令（非交互模式）
sndb sql "SELECT count(*) FROM cpu"

# 导入 CSV 文件
sndb import --measurement cpu --file data.csv

# 导出数据到 CSV
sndb export --measurement cpu --output data.csv
```

### REPL 交互模式

`sndb sql` 命令可以进入 REPL（Read-Eval-Print Loop）交互模式，提供一个类 SQL 终端的操作体验：

```bash
$ sndb sql
SonnetDB CLI v0.6.0
Connected to sonnetdb://localhost:8839
Type 'exit' to quit, 'help' for help

sndb> CREATE MEASUREMENT temperature (device TAG, value FIELD FLOAT);
OK

sndb> INSERT INTO temperature (time, device, value) VALUES (1713676800000, 'sensor-01', 23.5);
OK (1 row affected)

sndb> SELECT time, device, value FROM temperature WHERE device = 'sensor-01';
 time         | device     | value
 1713676800000 | sensor-01  | 23.5
(1 row)

sndb> exit
Goodbye!
```

REPL 模式下，您可以连续执行多个 SQL 语句，就像在数据库终端中操作一样。它还支持上下键浏览命令历史、Tab 键自动补全等便捷功能。

### 脚本模式

除了交互模式，`sndb` 还支持批量执行 SQL 脚本文件：

```bash
sndb execute --file init.sql
```

这对于自动化部署和 CI/CD 流水线集成非常有用。将数据库初始化脚本放到版本控制中，配合 `sndb execute` 命令，可以实现一键式的数据库环境搭建。

`sndb` CLI 工具覆盖了 SonnetDB 日常管理的绝大多数场景，从连接管理到数据导入导出，从 SQL 查询到自动化脚本，一应俱全。熟练掌握 `sndb` 的使用，可以显著提高您的工作效率。
