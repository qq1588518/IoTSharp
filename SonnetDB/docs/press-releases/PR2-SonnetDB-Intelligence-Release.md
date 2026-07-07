---
title: "SonnetDB 里程碑二：智能分析引擎发布——内置 50+ 函数、向量检索与地理空间分析"
date: 2026-04-25
---

# 新闻稿

## SonnetDB 里程碑二：智能分析引擎发布
### 内置 50+ 函数、PID 控制律、向量检索与地理空间轨迹分析

**[城市/日期]** —— 开源时序数据库 SonnetDB 今日宣布其智能分析引擎里程碑正式达成。该版本为 SonnetDB 增加了超过 50 个内建函数，涵盖统计聚合、窗口分析、向量检索、地理空间分析、PID 控制律和时序预测，将时序数据库从单纯的存储查询工具升级为全栈分析平台。

### 函数与运算符扩展（12 项 PR）

SonnetDB 建立了五层函数体系：

**Tier 1——标量函数**：abs、round、sqrt、log、coalesce，支持函数嵌套与算术表达式参数。

**Tier 2——扩展聚合函数**：基于 Welford 在线算法的 stddev/variance/spread，基于 T-Digest 的 percentile/p50/p90/p95/p99/median，基于 HyperLogLog 的 distinct_count，等宽直方图 histogram，以及向量质心 centroid。

**Tier 3——窗口函数框架**：17 个窗口函数，包括差分/变化量（difference、delta、increase）、变化率（derivative、rate、irate）、累积（cumulative_sum、integral）、平滑（moving_average、ewma、holt_winters）、空值处理（fill、locf、interpolate）、状态分析（state_changes、state_duration）。

### PID 控制律——数据库原生的工业控制

SonnetDB 是业界首个内置 PID 控制算法的时序数据库：

- **pid_series**：行级 PID 窗口函数，逐行输出控制量
- **pid**：桶级 PID 聚合函数，与 GROUP BY time 配合
- **pid_estimate**：三种自整定算法——Ziegler-Nichols、Cohen-Coon、Skogestad IMC

这意味着工程师可以直接在 SQL 中完成 PID 参数整定和实时控制，无需编写额外的控制程序。

### 向量检索——原生 AI 支持

- **VECTOR(dim) 数据类型**：任意维度的浮点向量存储
- **三种距离度量**：cosine_distance、l2_distance、inner_product
- **KNN 表值函数**：`knn(measurement, field, query_vector, k, metric)`
- **HNSW 索引**：`WITH INDEX hnsw(m=16, ef=200)` 加速 ANN 搜索
- **pgvector 兼容运算符**：`<=>`（余弦）、`<->`（L2）、`<#>`（内积）

### 地理空间与轨迹分析

- **GEOPOINT 数据类型**：原生地理坐标点存储
- **球面计算**：geo_distance（Haversine 距离）、geo_bearing（方位角）
- **空间过滤**：geo_within（圆形半径）、geo_bbox（矩形范围）
- **PostGIS 兼容**：ST_Distance、ST_Within、ST_DWithin
- **轨迹聚合**：trajectory_length、trajectory_centroid、trajectory_bbox、trajectory_speed_max/avg/p95
- **空间索引**：GeoHash32 段级剪枝过滤（Segment Format v5）
- **Web 地图**：MapLibre GL + ECharts 轨迹回放与速度图表

### 预测与异常检测

- **forecast TVF**：线性预测与 Holt-Winters 指数平滑预测
- **anomaly 窗口函数**：Z-Score、MAD（推荐）、IQR 三种异常检测
- **changepoint 窗口函数**：CUSUM 变点检测算法

### 用户自定义函数（UDF）

通过 C# API 可注册自定义的标量、聚合、窗口和表值函数，进一步扩展分析能力。

### 关于 SonnetDB

SonnetDB 是一款开源（MIT 许可证）时序数据库，专为 IoT 物联网、工业控制、运维监控和实时分析场景设计。项目地址：https://github.com/maikebing/SonnetDB

# # #
