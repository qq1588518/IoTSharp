## SonnetDB 路线图与未来展望：M17 可观测性、M18 VS Code 与社区共建

SonnetDB 自诞生以来，已经从一个概念验证项目成长为一个功能完善的时序数据库，拥有 MIT 许可证、活跃的社区和不断扩展的用户基础。站在 v0.6.0 的里程碑上，我们迫切需要分享未来的技术路线图，以及社区参与贡献的途径。

### M17 里程碑：可观测性增强

M17 版本将聚焦于 **可观测性（Observability）** 领域的能力增强，使 SonnetDB 更适合作为监控基础设施的核心组件：

```text
M17 核心特性规划
├── Prometheus 远端写入集成
│   ├── native Remote Write 接收器
│   └── 自动 Schema 映射
├── OpenTelemetry 支持
│   ├── OTLP gRPC/HTTP 接收器
│   ├── Trace 数据存储与查询
│   └── Span 关联分析
├── 内置监控仪表盘
│   ├── 实时写入/查询 QPS 监控
│   ├── 段文件数量与 Compaction 状态
│   └── MemTable 与缓存命中率
└── 告警引擎
    ├── SQL 驱动的告警规则配置
    ├── 多通道通知（Webhook、邮件、Slack）
    └── 告警静默与聚合
```

可观测性是一个自然的发展方向。SonnetDB 的高性能写入和灵活的查询能力使其天然适合作为 Metrics、Logs 和 Traces 的统一存储后端。以下是一个预想的告警规则配置示例：

```sql
-- 在 M17 中配置告警规则
CREATE ALERT high_cpu_usage
WITH (
    query = "SELECT avg(usage) AS avg_cpu FROM cpu WHERE time > now() - 600000",
    condition = "avg_cpu > 0.90",
    interval = 60000,
    channels = '["webhook:https://hooks.example.com/alert", "slack:#ops"]'
);
```

### M18 里程碑：开发者工具链

M18 将重点打造开发者工具链，VS Code 扩展的首个正式版本是其中的核心交付物：

```text
M18 核心特性规划
├── VS Code 扩展 v1.0
│   ├── 连接管理树（多连接支持）
│   ├── SQL 编辑器（智能提示、语法校验）
│   ├── 查询结果表格与图表视图
│   ├── Measurement 可视化设计器
│   └── 数据导入/导出向导
├── CLI 工具增强
│   ├── 交互式 REPL 模式
│   ├── 数据导出（CSV/JSON/Parquet）
│   └── 性能诊断命令
├── SDK 多语言扩展
│   ├── Python SDK 正式发布
│   ├── Node.js SDK 正式发布
│   └── Go SDK 预览
└── 文档与教程
    ├── 交互式 Playground 环境
    ├── 中文技术博客系列
    └── 视频教程
```

其中，Python SDK 的 API 设计草案如下：

```python
# SonnetDB Python SDK（M18 预览）
from sonnetdb import SonnetDBClient

client = SonnetDBClient(host="localhost", port=3260)

# 批量写入数据
points = [
    {"measurement": "cpu", "tags": {"host": "s1"},
     "fields": {"usage": 0.75}, "time": 1713676800000},
    {"measurement": "cpu", "tags": {"host": "s2"},
     "fields": {"usage": 0.82}, "time": 1713676800000},
]
client.write_points(points)

# 查询并直接转换为 Pandas DataFrame
df = client.query_to_dataframe(
    "SELECT time, host, usage FROM cpu WHERE host = 's1'")
```

### 社区贡献指南

SonnetDB 是一个由社区驱动的开源项目，我们非常欢迎各类形式的贡献：

**代码贡献**：项目托管在 GitHub，使用标准 Fork + PR 流程。代码规范遵循 .NET 官方编码约定，所有 PR 需要包含对应的单元测试和集成测试。

```bash
# 参与 SonnetDB 开发
git clone https://github.com/maikebing/SonnetDB.git
cd SonnetDB
dotnet build
dotnet test

# 查找 Good First Issue
# https://github.com/maikebing/SonnetDB/issues?q=is:open+label:"good+first+issue"
```

**非代码贡献**：不擅长编写代码同样可以参与贡献——撰写技术博客、翻译文档、报告 Bug、在社区回答其他用户的问题，都是非常有价值的贡献方式。项目使用 GitHub Discussions 作为社区讨论的主要平台。

### 长期愿景

放眼更远的未来，SonnetDB 的目标是成为**时序数据领域的基础设施标准**。我们计划在后续版本中探索分布式集群支持、跨数据中心复制、ML 驱动的智能索引和自适应压缩等前沿技术。无论您是开发者、运维人员还是技术布道者，SonnetDB 社区都欢迎您的加入。让我们一起打造更好用的开源时序数据库。
