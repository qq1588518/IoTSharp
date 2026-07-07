# SonnetDB 博客文章发布计划

> 共 128 篇 · 按主题分组 · 建议发布节奏：每周 3-5 篇

---

## 第一阶段：产品认知（第 1-12 篇）

| # | 标题 | 内容要点 | 建议日期 |
|---|------|---------|----------|
| 1 | SonnetDB 简介：下一代开源时序数据库 | 产品定位、核心特性、适用场景、与同类对比 | D1 |
| 2 | 为什么选择 SonnetDB：五大核心优势 | 嵌入式优先、50+内建函数、向量检索、PID控制、AI Copilot | D3 |
| 3 | 五分钟上手：Docker 部署 SonnetDB | Docker 拉取、启动、首次安装向导全流程 | D5 |
| 4 | 从源码构建 SonnetDB 开发环境 | git clone、dotnet build、VS Code/Rider 配置 | D7 |
| 5 | SonnetDB 架构深度解析 | 写入路径、查询路径、WAL/MemTable/Segment 分层 | D9 |
| 6 | 时序数据库选型指南：SonnetDB vs InfluxDB vs TDengine vs SQLite | 性能对比、功能对比、场景推荐；补充 SonnetDB Server vs IoTDB Server 同口径写入对比（1.98x，22867 vs 11541 val/s） | D11 |
| 7 | 安装 SonnetDB CLI 工具 | `dotnet tool install`、配置文件管理、REPL 模式 | D13 |
| 8 | 首次安装向导：从零开始配置 SonnetDB 服务器 | 管理员创建、Token 生成、数据库创建 | D15 |
| 9 | 了解 SonnetDB 的数据模型 | Measurement、Tag、Field、Time、Series 概念解析 | D17 |
| 10 | 目录式持久化：了解 SonnetDB 的文件格式 | catalog/tombstones/WAL/Segment 的目录布局 | D19 |
| 11 | SonnetDB 的安全机制：用户、角色与权限 | readonly/readwrite/admin、GRANT/REVOKE | D21 |
| 12 | Token 认证：保护你的 SonnetDB 实例 | ISSUE TOKEN、BEARER 认证、Token 吊销 | D23 |

## 第二阶段：SQL 基础（第 13-30 篇）

| # | 标题 | 内容要点 | 建议日期 |
|---|------|---------|----------|
| 13 | 使用 CREATE MEASUREMENT 定义时序表 | TAG/FIELD 类型、VECTOR/GEOPOINT 高级类型 | D25 |
| 14 | 创建带 HNSW 向量索引的表 | WITH INDEX hnsw(m, ef) 语法、参数选择 | D27 |
| 15 | 写入时序数据：INSERT INTO 详解 | 单行/多行、time 省略、不同类型字段写入 | D29 |
| 16 | 使用 POINT(lat, lon) 写入地理空间数据 | GEOPOINT 类型的数据写入方式 | D31 |
| 17 | 使用向量字面量写入向量数据 | VECTOR 类型的 `[v0, v1, ...]` 语法 | D33 |
| 18 | 基础查询：SELECT 与 WHERE 过滤 | `SELECT *`、投影、tag 过滤、时间范围 | D35 |
| 19 | SonnetDB SQL 中的算术表达式 | 投影中直接加减乘除、一元负号 | D37 |
| 20 | 标量函数的使用：abs/round/sqrt/log/coalesce | 数学函数与空值处理函数详解 | D39 |
| 21 | 函数嵌套调用：构建复杂的计算表达式 | `round(abs(usage - 0.5), 2)` 模式 | D41 |
| 22 | SQL 分页查询：LIMIT/OFFSET 与 FETCH 语法 | 两种分页风格详解 | D43 |
| 23 | 多条件过滤：AND 连接多个 WHERE 约束 | tag+时间范围联合过滤 | D45 |
| 24 | 查询元数据：SHOW 与 DESCRIBE 的使用 | SHOW MEASUREMENTS/TABLES/USERS/GRANTS/TOKENS/DATABASES | D47 |
| 25 | 删除数据：SonnetDB 的 DELETE 与 Tombstone 机制 | 时间范围删除、tag 删除、Tombstone 生命周期 | D49 |
| 26 | 注释语法：SonnetDB SQL 支持的四种注释方式 | `--`、`//`、`/* */`、`REM` | D51 |
| 27 | 标识符引用：双引号的使用场景 | `"column_name"` 语法 | D53 |
| 28 | 标量向量函数：cosine_distance/l2_distance/inner_product/vector_norm | 四种向量距离函数详解 | D55 |
| 29 | pgvector 兼容运算符：<=> <-> <#> | 运算符语法与函数语法的等价性 | D57 |
| 30 | SQL Cookbook：常用查询模式速查 | 时序查询的 10 个典型场景 | D59 |

## 第三阶段：聚合与分析（第 31-48 篇）

| # | 标题 | 内容要点 | 建议日期 |
|---|------|---------|----------|
| 31 | 基础聚合函数：count/sum/min/max/avg/first/last | 七种基础聚合详解与使用场景 | D61 |
| 32 | 统计聚合函数：stddev/variance/spread/median/mode | Welford 在线算法、应用场景 | D63 |
| 33 | 深入 T-Digest：分位数聚合与 percentile | p50/p90/p95/p99、tdigest_agg 内部状态 | D65 |
| 34 | 去重计数：distinct_count 与 HyperLogLog 算法 | 基数估计原理与使用 | D67 |
| 35 | 直方图聚合：histogram 与数据分布分析 | 等宽分桶、分布洞察 | D69 |
| 36 | 向量质心聚合：centroid 的妙用 | 向量均值、聚类中心计算 | D71 |
| 37 | GROUP BY time：时间桶聚合入门 | 桶时长单位、聚合函数配合 | D73 |
| 38 | 时间桶聚合进阶：多维度聚合分析 | 组合多个聚合函数的实战案例 | D75 |
| 39 | 窗口函数入门：difference 与 delta | 行间差分、变化量计算 | D77 |
| 40 | 计数器增量分析：increase 函数 | 抑制计数器回零毛刺 | D79 |
| 41 | 变化率计算：derivative/non_negative_derivative/rate/irate | 四种变化率函数详解与差异 | D81 |
| 42 | 累积分析：cumulative_sum 与 integral | 累积求和与梯形积分 | D83 |
| 43 | 数据平滑技术：moving_average 与 ewma | 移动平均与指数加权移动平均 | D85 |
| 44 | 双指数平滑：holt_winters 函数详解 | Holt 加法模型、趋势平滑 | D87 |
| 45 | 空值处理：fill/locf/interpolate 三种填充策略 | 常量填充、前向填充、线性插值 | D89 |
| 46 | 状态分析：state_changes 与 state_duration | 状态变化检测、持续时长计算 | D91 |
| 47 | 窗口函数综合应用：构建完整的监控分析管道 | 差分→异常检测→告警的工作流 | D93 |
| 48 | INSERT 批量写入性能优化技巧 | 多行 VALUES、flush 模式选择 | D95 |

## 第四阶段：高级功能（第 49-73 篇）

| # | 标题 | 内容要点 | 建议日期 |
|---|------|---------|----------|
| 49 | 向量检索入门：KNN 表值函数详解 | knn() 语法、距离度量、结果解读 | D97 |
| 50 | KNN 实战：构建语义搜索系统 | 带 tag 过滤、时间范围过滤 | D99 |
| 51 | HNSW 索引：加速你的向量搜索 | ANN 原理、m/ef 参数调优、Recall 基准 | D101 |
| 52 | 向量检索性能调优：暴力搜索 vs HNSW | 精确率 vs 延迟的权衡 | D103 |
| 53 | 向量距离度量选择指南：cosine vs L2 vs inner_product | 不同度量的数学含义与场景 | D105 |
| 54 | 向量召回评估：Recall@10 基准测试 | 精度衡量、HNSW 参数影响 | D107 |
| 55 | 从 pgvector 迁移到 SonnetDB | 运算符兼容性、SQL 语法差异 | D109 |
| 56 | 地理空间入门：GEOPOINT 类型与 POINT 字面量 | 创建含 GEOPOINT 的表、写入数据 | D111 |
| 57 | 提取地理坐标：lat 与 lon 函数 | 从 GEOPOINT 中分离经纬度 | D113 |
| 58 | 球面距离与方位角：geo_distance 与 geo_bearing | Haversine 算法、距离/方向计算 | D115 |
| 59 | 空间范围过滤：geo_within 与 geo_bbox | 圆形半径过滤与矩形过滤 | D117 |
| 60 | PostGIS 兼容迁移指南：ST_Distance/ST_Within/ST_DWithin | 从 PostGIS 迁移到 SonnetDB | D119 |
| 61 | 瞬时速度计算：geo_speed 函数 | 利用相邻点经纬度+时间差算速度 | D121 |
| 62 | 轨迹分析：六大轨迹聚合函数详解 | trajectory_length/centroid/bbox/speed | D123 |
| 63 | PID 控制入门：pid_series 窗口函数 | PID 控制原理、在 SQL 中使用 | D125 |
| 64 | 桶级 PID 控制：pid 聚合函数 | GROUP BY time + PID 的工业场景 | D127 |
| 65 | PID 参数自整定：pid_estimate 的三种方法 | IMC/Ziegler-Nichols/Cohen-Coon 对比 | D129 |
| 66 | PID 整定实战：从阶跃响应到最优参数 | 阶跃信号分析、整定结果解读 | D131 |
| 67 | 工业 IoT 中的 PID 控制：SonnetDB 的独特优势 | 传统 PLC vs 时序数据库 PID | D133 |
| 68 | PID 控制笔记：Kp/Ki/Kd 参数调整经验 | 各参数作用、调参口诀、常见问题 | D135 |
| 69 | 时序预测入门：forecast 表值函数 | 线性预测与 Holt-Winters 算法 | D137 |
| 70 | 异常检测：三种方法的原理与选择 | Z-Score/MAD/IQR 对比 | D139 |
| 71 | 异常检测实战：构建监控告警系统 | 实时异常检测管道、阈值选择 | D141 |
| 72 | CUSUM 变点检测：发现数据中的结构性变化 | 变点检测原理、参数调优 | D143 |
| 73 | 预测+异常检测+变点：完整的数据分析流水线 | 三者联合使用的实战案例 | D145 |

## 第五阶段：AI 与生态（第 74-100 篇）

| # | 标题 | 内容要点 | 建议日期 |
|---|------|---------|----------|
| 74 | SonnetDB Copilot 介绍：你的 AI 数据库管家 | Copilot 能做什么、架构概述 | D147 |
| 75 | 配置 Copilot：支持的 AI 提供程序 | OpenAI/DashScope/ZhiPu/Moonshot/DeepSeek 配置 | D149 |
| 76 | Copilot 知识库：自动文档索引与检索 | 知识库构建、文档摄入管道 | D151 |
| 77 | Copilot 技能库：6 个内置技能详解 | 查询聚合/PID调优/预测/慢查询排查/Schema设计/批量写入 | D153 |
| 78 | 多轮对话：与 Copilot 协作编写 SQL | 对话上下文、SQL 自纠正机制 | D155 |
| 79 | CopilotDock：Web 界面中的浮动 AI 面板 | 页面感知、读写模式、模型选择、会话历史 | D157 |
| 80 | 嵌入式集成：在 C# 应用中使用 SonnetDB | Tsdb.Open、SqlExecutor、进程内数据库 | D159 |
| 81 | ADO.NET 入门：使用 SndbConnection 连接 SonnetDB | 连接字符串、执行查询、DataReader | D161 |
| 82 | ADO.NET 远程连接：通过 HTTP 访问远程 SonnetDB | sonnetdb+http:// 协议、Token 认证 | D163 |
| 83 | ADO.NET 批量写入：使用 TableDirect 命令类型 | 高性能批量导入 | D165 |
| 84 | 用户自定义函数：扩展 SonnetDB 的功能 | RegisterScalar/RegisterAggregate/RegisterWindow | D167 |
| 85 | CLI 进阶：使用 sndb 命令行管理数据库 | REPL 交互模式、配置文件管理、远程执行 | D169 |
| 86 | HTTP API 完整参考：所有 REST 端点详解 | 数据面、控制面、MCP 端点全集 | D171 |
| 87 | 三种批量写入格式对比：LP vs JSON vs Bulk | 格式差异、性能对比、场景选择 | D173 |
| 88 | Flush 模式选择：同步、异步与不 flush | 写入性能 vs 数据持久化的权衡 | D175 |
| 89 | 使用 Line Protocol 高效写入 | InfluxDB 兼容格式、批量导入 | D177 |
| 90 | 生产部署：Docker Compose 与安装器使用 | docker-compose.yml、MSI/DEB/RPM | D179 |
| 91 | 性能揭秘：SonnetDB 如何实现 180 万点/秒写入 | WriteMany 批处理、零拷贝路径 | D181 |
| 92 | 5 款时序数据库性能大对决 | SonnetDB vs SQLite vs InfluxDB vs TDengine 全面基准 | D183 |
| 93 | 范围查询为何如此之快？ | Segment 跳过、Block 元数据、向量化 | D185 |
| 94 | 聚合查询性能优化指南 | 跨桶融合、MemTable 增量聚合 | D187 |
| 95 | 基准测试方法论：如何正确地比较时序数据库 | BenchmarkDotNet、统一数据、多次运行；强调嵌入式与服务端链路必须同口径比较，实测 1.98x 案例 | D189 |
| 96 | Segment 压缩：Size-Tiered 策略详解 | 压缩触发条件、合并策略、IO 优化 | D191 |
| 97 | 数据保留策略：TTL 自动过期删除 | Retention Worker、自动注入 Tombstone | D193 |
| 98 | MCP 协议集成：将 SonnetDB 接入任意 AI 应用 | MCP 工具列表、权限模型 | D195 |
| 99 | SonnetDB for VS Code：数据库管理的未来（预览） | 扩展功能预览、开发路线图 | D197 |
| 100 | SonnetDB 路线图：下一个版本的新功能预览 | M17 Observability、M18 VS Code 扩展、社区贡献指南 | D199 |

## 第六阶段：行业应用案例（第 101-110 篇）

> 以真实场景为背景，讲述 SonnetDB 在各行业的落地实践，每篇包含：背景、挑战、解决方案、关键 SQL 示例、实施效果。

| # | 标题 | 行业/场景 | 建议日期 |
| --- | ------ | --------- | ---------- |
| 101 | 案例：IoT 平台如何用 SonnetDB 管理百万设备实时数据 | IoT 平台/边缘计算 | D201 |
| 102 | 案例：智能制造——汽车总装线传感器数据采集与 PID 控制 | 工业制造 | D203 |
| 103 | 案例：光伏电站运维——基于 SonnetDB 的发电量异常检测 | 新能源/能源监控 | D205 |
| 104 | 案例：楼宇自动化——SonnetDB 助力智慧园区能耗管理 | 楼宇/智慧城市 | D207 |
| 105 | 案例：冷链物流——全程温湿度监控与超标告警系统 | 物流/冷链 | D209 |
| 106 | 案例：城市交通监控——路口车流量时序分析与拥堵预测 | 智慧交通 | D211 |
| 107 | 案例：数据中心——服务器集群的指标采集与容量预测 | 运维监控/可观测性 | D213 |
| 108 | 案例：农业 IoT——温室大棚环境数据采集与智能灌溉 | 智慧农业 | D215 |
| 109 | 案例：设备预测性维护——振动信号分析与故障预警 | 工业维护 | D217 |
| 110 | 案例：用 SonnetDB + AI Copilot 构建无代码数据分析平台 | AI 应用/SaaS | D219 |

## 第七阶段：基准测试与 v0.6.0 新特性（第 111-117 篇）

| # | 标题 | 内容要点 | 建议日期 |
|---|------|---------|----------|
| 111 | 基准测试：SonnetDB 插入性能深度报告 | 单点/批量插入吞吐量、不同数据类型对比；包含 SonnetDB Server vs IoTDB Server 同口径对比（AB BA ×4，平均 1.98x，CLI: --comparison-server） | D221 |
| 112 | 基准测试：SonnetDB 查询性能深度报告 | 范围查询、过滤查询延迟对比 | D223 |
| 113 | 基准测试：SonnetDB 聚合性能深度报告 | 常见聚合函数性能、跨桶聚合效率 | D225 |
| 114 | 基准测试：SonnetDB 地理空间性能深度报告 | GEOPOINT 类型读写性能、空间查询延迟 | D227 |
| 115 | 基准测试：SonnetDB PID 控制性能深度报告 | PID 计算吞吐量、实时控制场景模拟 | D229 |
| 116 | 基准测试：SonnetDB 向量搜索性能深度报告 | HNSW 索引构建、knn() 查询延迟、召回率 | D231 |
| 117 | Schema-on-Write：可控的字段自动创建 | 自动 Tag/Field 创建、宽松 vs 严格模式、生产环境最佳实践 | D233 |

## 第八阶段：SQL 兼容性增强（第 118-120 篇）

| # | 标题 | 内容要点 | 建议日期 |
|---|------|---------|----------|
| 118 | SQL 兼容性基础：SELECT 1 与 count(1) 支持 | 字面量投影、count(1) 与 count(*) 等价、ORM 兼容 | D235 |
| 119 | ORDER BY 排序支持：让时序查询井然有序 | ORDER BY time ASC/DESC 语法、解析器与执行器实现 | D237 |
| 120 | 单表别名与 DDL 修饰符：写出更地道的 SQL | alias.column 限定列名、AS/无 AS 语法、NOT NULL/DEFAULT 框架 | D239 |

## 第九阶段：多语言连接器（第 121-122 篇）

| # | 标题 | 内容要点 | 建议日期 |
|---|------|---------|----------|
| 121 | SonnetDB C 连接器：嵌入式时序数据库的原生接入 | C ABI 设计、17 个导出函数、构建与平台支持 | D241 |
| 122 | SonnetDB Java 连接器：一套 API 双后端驱动 | JNI vs FFM 架构对比、多版本 JAR、Java 8/21 自适应 | D243 |

## 第十阶段：性能与可靠性工程化（第 123-128 篇）

| # | 标题 | 内容要点 | 建议日期 |
|---|------|---------|----------|
| 123 | Segment v6 与崩溃恢复：把可靠性做到文件尾部 | v6 extension section、mini-footer、v4/v5 兼容读取 | D245 |
| 124 | 查询热路径优化：索引、缓存与少一点 LINQ | SegmentReader series/time index、reader map 缓存、tombstone 手写过滤、block 解码缓存 | D247 |
| 125 | MemTable 优化：从热路径统计到快照并发 | 增量 EstimatedBytes、字符串 byte count、snapshot 缓存、range 二分裁剪 | D249 |
| 126 | 窗口函数执行器：从 object 数组走向 typed streaming | typed evaluator、IWindowState、Span 批量路径、窗口函数降分配 | D251 |
| 127 | 读多写少结构治理：FrozenDictionary、Lexer 快路径与 Analyzer | catalog/tag index 冻结快照、options record、SearchValues lexer、性能 analyzer | D253 |
| 128 | Codec 专用化：BlockDecoder 为什么选择手写 fast path | source generator/泛型/手写方案评估、DecodeInto、BenchmarkDotNet 对比 | D255 |
