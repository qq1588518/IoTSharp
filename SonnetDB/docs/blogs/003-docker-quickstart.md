## Docker 快速上手：5 分钟运行 SonnetDB

如果您想快速体验 SonnetDB 的功能而不想处理编译和配置的细节，Docker 无疑是最便捷的途径。本文将引导您在 5 分钟内完成 SonnetDB 的 Docker 部署，并访问其管理界面。

### 第一步：拉取镜像

SonnetDB 的 Docker 镜像托管在 Docker Hub 上，您可以直接使用 `docker pull` 命令获取最新版本。打开终端，执行以下命令：

```bash
docker pull sonnetdb/sonnetdb:latest
```

镜像包含了完整的 SonnetDB 服务端程序及其运行依赖。目前发布版本基于 .NET 10 构建，支持 linux/amd64 和 linux/arm64 两种架构，这意味着您可以在 x86 服务器和 ARM 设备（如树莓派）上无缝运行。

### 第二步：启动容器

拉取完成后，使用以下命令启动 SonnetDB 容器：

```bash
docker run -d \
  --name sonnetdb \
  -p 8839:8839 \
  -p 8840:8840 \
  -v sonnetdb-data:/data \
  sonnetdb/sonnetdb:latest
```

这里解释一下参数的含义：`-p 8839:8839` 映射了 SonnetDB 的 HTTP API 端口，应用程序通过此端口与数据库通信；`-p 8840:8840` 映射了 Web 管理界面的端口；`-v sonnetdb-data:/data` 创建了一个数据卷来持久化存储数据库文件，确保即使容器被删除，数据也不会丢失。

如果您希望在宿主机特定目录存储数据，也可以使用 bind mount 方式：

```bash
docker run -d \
  --name sonnetdb \
  -p 8839:8839 \
  -p 8840:8840 \
  -v /opt/sonnetdb/data:/data \
  sonnetdb/sonnetdb:latest
```

### 第三步：访问管理界面

容器启动后，打开浏览器访问 `http://localhost:8840`，您将看到 SonnetDB 的管理界面。首次访问时，系统会引导您进入 **首次设置向导（First Setup Wizard）**，在这里您需要创建管理员用户并获取访问令牌。这个过程我们在后续的文章中会详细介绍。

管理界面提供了一系列便捷的功能：SQL 查询控制台、数据可视化、测量管理、用户和令牌管理、系统监控仪表盘等。对于不熟悉命令行操作的开发者来说，图形化界面大大降低了使用门槛。

### 第四步：通过 API 验证连接

除了 Web 界面，您也可以通过命令行验证 SonnetDB 是否正常运行：

```bash
# 检查健康状态
curl http://localhost:8839/health

# 返回示例
{"status":"healthy","version":"0.6.0","uptime":"1h32m"}
```

### Docker Compose 方式

对于生产环境，推荐使用 Docker Compose 进行更精细的配置管理。创建一个 `docker-compose.yml` 文件：

```yaml
version: '3.8'
services:
  sonnetdb:
    image: sonnetdb/sonnetdb:latest
    container_name: sonnetdb
    ports:
      - "8839:8839"
      - "8840:8840"
    volumes:
      - ./data:/data
    restart: unless-stopped
    environment:
      - TZ=Asia/Shanghai
```

然后执行 `docker compose up -d` 即可一键启动。

至此，您的 SonnetDB 实例已经成功运行。接下来，您可以尝试使用 `CREATE MEASUREMENT` 创建第一个时序表，或者通过 SQL 控制台执行查询，亲身体验 SonnetDB 的功能与性能。
