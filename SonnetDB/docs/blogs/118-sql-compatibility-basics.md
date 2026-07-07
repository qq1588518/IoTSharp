## SQL 兼容性基础：SELECT 1 与 count(1) 支持

SonnetDB 的 SQL 引擎在设计之初就遵循 SQL 标准，但随着实际应用的深入，我们发现一些在主流数据库中被广泛使用的"非标准写法"同样需要支持。这些写法虽然不属于严格的 SQL 标准，但却是 ORM 框架、数据库探活工具和 AI Copilot 生成代码中的常见模式。本次更新对两类典型场景进行了兼容性增强：**字面量投影（SELECT 1）**和 **count(1) 聚合**。

### SELECT 1 FROM ... LIMIT 1：数据库探活的标配

在数据库运维和开发中，`SELECT 1` 是最经典的连接探活语句。许多连接池（如 HikariCP、DBCP）和健康检查端点都会执行 `SELECT 1` 来验证数据库连接是否可用。在 SonnetDB 之前的版本中，这种写法会因为缺少 FROM 子句中的列引用而令人困惑——毕竟在一个时序数据库中，`SELECT 1` 究竟应该返回什么？

答案很简单：对于时序数据，`SELECT 1` 会为结果集中的每一行返回常量值 `1`。由于 SonnetDB 的查询引擎始终以时间序列为驱动轴，`SELECT 1 FROM cpu LIMIT 1` 会返回 cpu 测量中第一个时间点对应的行，该行的值为整数 `1`。

```sql
-- 连接探活
SELECT 1 FROM cpu LIMIT 1;

-- 带别名的字面量投影
SELECT 1 AS ok FROM cpu LIMIT 1;

-- 多种字面量类型均支持
SELECT 'healthy' AS status, 1 AS code FROM cpu LIMIT 1;
```

实现层面，SonnetDB 的手写递归下降解析器原生支持表达式解析，因此整数、浮点数、字符串、布尔值和 NULL 字面量在 SELECT 子句中开箱即用。执行器将字面量识别为 `ProjectionKind.Constant`（常量投影），在每行结果中填入相同的值。这使得 `SELECT 1` 不需要任何特殊的硬编码处理，与其他表达式共享同一代码路径。

### count(1)：与 count(*) 等价

`COUNT(1)` 是 SQL 社区中一个久负盛名的写法。关于 `COUNT(*)` 和 `COUNT(1)` 哪个更快的争论持续了数十年，尽管在现代数据库中两者性能完全一致。许多 ORM（如 Entity Framework、Hibernate）和 AI 代码生成工具在生成计数查询时习惯使用 `count(1)`。

在本次更新中，SonnetDB 的 `count` 聚合函数新增了对 `count(1)` 的支持，其语义与 `count(*)` 完全等价——统计所有 field 值的数量，而非字面意思上的"数 1 的个数"。

```sql
-- 以下三条查询语义完全一致
SELECT count(*) FROM cpu WHERE host = 'edge-1';
SELECT count(1) FROM cpu WHERE host = 'edge-1';
SELECT count(usage) FROM cpu WHERE host = 'edge-1';
```

内部实现非常简洁：当函数注册器检测到 `count` 的参数为整数常量 `1` 时，直接返回 `null` 作为字段名（与 `count(*)` 的处理完全一致），从而将 `AggSpec.IsCountStar` 标记为 `true`。后续的聚合循环将其作为全列计数处理。

这一兼容性改进意味着，AI Copilot 或 ORM 自动生成的 SQL 可以直接在 SonnetDB 上执行，无需手动改写。

### 小结

这两项兼容性增强看似微小，却显著提升了 SonnetDB 与现有生态工具的互操作性。`SELECT 1` 让连接池和健康检查无缝接入，`count(1)` 消除了 ORM 生成 SQL 的改写负担。这正是 SonnetDB 在追求 SQL 标准兼容道路上的务实态度：既遵循标准，也拥抱生态。
