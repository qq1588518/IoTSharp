## SonnetDB 生产部署指南：Docker Compose、安装包与环境变量

SonnetDB 提供多种生产部署方式，覆盖 Docker 容器化、操作系统原生安装包（MSI/DEB/RPM）以及环境变量配置，满足从单机到生产集群的不同运维需求。

### Docker Compose 部署

项目根目录提供开箱即用的 `docker-compose.yml`，一行命令即可启动：

```yaml
services:
  sonnetdb:
    image: iotsharp/sonnetdb:latest
    ports:
      - "5080:5080"
    volumes:
      - sonnetdb-data:/data
    environment:
      SONNETDB_SonnetDBServer__DataRoot: /data
      SONNETDB_SonnetDBServer__AutoLoadExistingDatabases: "true"
      SONNETDB_SonnetDBServer__Tokens__admin: sndb_your_admin_token
    restart: unless-stopped

volumes:
  sonnetdb-data:
```

```bash
docker compose up -d
```

首次访问 `http://localhost:5080/admin/` 进入安装向导，设置服务器 ID、组织名和管理员密码。也可通过环境变量 `SONNETDB_SonnetDBServer__Tokens__<name>` 预设 Token 跳过向导。

### 原生安装包：MSI / DEB / RPM

SonnetDB 通过 GitHub Actions 自动打包发布三大平台原生安装包：

```bash
# Debian / Ubuntu
sudo dpkg -i sonnetdb_0.1.0_amd64.deb
sudo systemctl start sonnetdb
sudo systemctl enable sonnetdb

# Red Hat / CentOS / Fedora
sudo rpm -ivh sonnetdb-0.1.0-1.x86_64.rpm
sudo systemctl start sonnetdb

# Windows
msiexec /i SonnetDB-0.1.0-x64.msi /quiet
```

安装包自动注册系统服务、创建专用运行用户，数据目录默认位于 `/var/lib/sonnetdb/`。

### 环境变量配置

SonnetDB 遵循 .NET 配置层次结构，键名使用双下划线 `__` 分隔层级：

```bash
# 数据目录
SONNETDB_SonnetDBServer__DataRoot=/var/lib/sonnetdb

# 认证：预设多个 Token
SONNETDB_SonnetDBServer__Tokens__admin=sndb_admin_token
SONNETDB_SonnetDBServer__Tokens__readonly=sndb_ro_token

# 自动加载已有数据库
SONNETDB_SonnetDBServer__AutoLoadExistingDatabases=true

# Copilot AI 助手配置
SONNETDB_SonnetDBServer__Copilot__Enabled=true
SONNETDB_SonnetDBServer__Copilot__Embedding__Provider=openai
SONNETDB_SonnetDBServer__Copilot__Chat__Endpoint=https://api.openai.com/v1
SONNETDB_SonnetDBServer__Copilot__Chat__ApiKey=${OPENAI_API_KEY}

# 可观测性
SONNETDB_Observability__Prometheus__Enabled=true
```

### CLI 全局工具

.NET 开发者可通过 dotnet tool 安装命令行客户端：

```bash
dotnet tool install --global SonnetDB.Cli --version 0.1.0
sndb connect production --url http://sonnetdb.mycompany.com:5080 --repl
```

### 生产建议

- 使用卷挂载持久化 `/data`，容器重建不丢数据
- 预设 Bearer Token 确保接口安全，避免匿名访问
- 启用 `/healthz` 健康检查集成到容器编排
- 接入 Prometheus 端点监控写入吞吐、Compaction 和内存用量
- 定期备份 `.SDBCAT`、`.SDBSEG` 和 `.SDBWAL` 文件
