## 六大内置技能：从聚合查询到批量导入的专家知识

SonnetDB Copilot 的核心竞争力之一是其可插拔的技能库系统。每个技能是一个带 YAML frontmatter 的 Markdown 文件，存放在 `copilot/skills/` 目录中。系统内置 24 个技能，以下重点介绍 6 个核心技能。

### 技能定义格式

每个技能文件以 `---` 分隔的 YAML 元数据开头，声明名称、描述、触发关键词和建议工具：

```yaml
---
name: query-aggregation
description: 编写时间窗口聚合查询的最佳实践与常见反模式
triggers:
  - aggregation
  - group by
  - 聚合
requires_tools:
  - query_sql
  - describe_measurement
---
```

`SkillFrontmatter` 解析器不依赖外部 YAML 库，纯字符串解析，保持零依赖设计哲学。

### 1. query-aggregation —— 时间窗口聚合查询

指导 LLM 编写正确的 `GROUP BY time(...)` 聚合查询。核心规则包括：始终带时间过滤、显式给聚合列起别名、根据查询区间选择合适的桶大小。同时列出了常见反模式——全表无过滤聚合、跨 tag 聚合等。

```sql
SELECT avg(temperature) AS avg_temp, count(*) AS samples
FROM cpu WHERE host = 'server-01' AND time >= now() - 1h
GROUP BY time(1m);
```

### 2. pid-control-tuning —— PID 参数整定

针对工业物联网场景，指导如何使用 SonnetDB 内置的 `pid_step()` 和 `pid_replay()` 函数做闭环控制。给出了 Ziegler-Nichols 整定流程、时间常数估算方法以及抗积分饱和注意事项。

### 3. forecast-howto —— 时序预测

`forecast()` 表值函数支持线性外推和 Holt-Winters 指数平滑两种算法。技能详细说明了算法选择策略：单调趋势用 `'linear'`，有季节性波动用 `'holt_winters'`，数据少时优先用线性。

```sql
SELECT time, value, lower, upper
FROM forecast(host_cpu, cpu_pct, 5, 'linear')
WHERE host = 'web-01';
```

### 4. troubleshoot-slow-query —— 慢查询排查

标准化的性能排查流程：先检查时间过滤，再看 tag 等值条件，确认高基数列的 schema 设计，最后分析返回行数是否需要聚合。

| 现象 | 定位 |
|------|------|
| WHERE 没有 time 过滤 | 全 segment 扫描 |
| 大量 LIKE 在 string field | 改 tag 或预聚合 |
| 返回 > 10w 行 | 缺聚合或 LIMIT |

### 5. schema-design —— 表结构设计

回答"这个字段做 tag 还是 field"等建模问题。核心建议：维度用 tag，数值采样用 field，基数是关键决策点——单 measurement 唯一 tag 组合数超过 100 万时应拆表。

### 6. bulk-ingest —— 批量导入

大批量历史数据回填的最佳实践：使用 `/v1/bulk/` 接口、单批 5w-50w 行、4-8 路并发、按 tag 分桶避免乱序。强调不要用单点 INSERT 做大批量导入。

### 检索与加载

技能通过 `SkillSourceScanner` 扫描目录，`SkillRegistry` 将其嵌入 `__copilot__.skills` 表。运行时，`SkillSearchService` 做向量检索找到 Top-K 技能，Agent 仅加载最多 3 个技能进入上下文，由 `SkillRegistry.Load(name)` 返回完整 markdown 正文。
