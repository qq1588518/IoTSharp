## SQL 分页查询：LIMIT/OFFSET 与 FETCH 语法

在处理大量时序数据时，分页查询是必不可少的工具。SonnetDB 同时支持两种分页语法风格：传统的 `LIMIT ... OFFSET` 和 SQL 标准 `OFFSET ... FETCH`，让你可以根据习惯自由选择。

### 风格一：LIMIT ... OFFSET（传统风格）

这种风格源自 PostgreSQL 和 MySQL，语法简洁直观，是最常用的分页方式：

```sql
SELECT time, host, usage
FROM cpu
WHERE host = 'server-01'
ORDER BY time ASC
LIMIT 5 OFFSET 0;
```

`OFFSET 0` 表示跳过 0 行（即从第一行开始），`LIMIT 5` 表示最多返回 5 行。要获取第二页数据，只需将 OFFSET 调整为 5：

```sql
SELECT time, host, usage
FROM cpu
WHERE host = 'server-01'
ORDER BY time ASC
LIMIT 5 OFFSET 5;
```

### 风格二：OFFSET ... FETCH（SQL 标准风格）

这种风格符合 SQL:2008 标准，语义更加清晰自文档化：

```sql
SELECT time, host, usage
FROM cpu
WHERE host = 'server-01'
ORDER BY time ASC
OFFSET 5 ROWS FETCH NEXT 5 ROWS ONLY;
```

这条查询的含义是：跳过前 5 行，然后获取接下来的 5 行。`OFFSET ... ROWS` 指定跳过的行数，`FETCH NEXT ... ROWS ONLY` 限制返回的行数。

### 两种风格对比

两种语法在功能上是等价的，但在以下方面有所差异：

| 特性 | LIMIT ... OFFSET | OFFSET ... FETCH |
|------|-----------------|-----------------|
| 语法简洁性 | 更简洁 | 更冗长但自文档 |
| 标准兼容性 | 非标准但广泛支持 | SQL:2008 标准 |
| 省略 OFFSET | `LIMIT 5` 即可 | 需写完整 |
| 可读性 | 适合简单分页 | 适合正式代码 |

### 分页的最佳实践

在时序数据库中执行分页查询时，有几个要点需要注意：

首先，**始终使用 ORDER BY time**。时序数据的天然排序是时间，没有 `ORDER BY` 的分页结果是不可预测的。建议固定按 `time ASC` 排序，确保分页结果的一致性。

其次，**大偏移量可能带来性能问题**。`OFFSET 100000 LIMIT 10` 虽然只返回 10 行，但数据库仍需要扫描前 100000 行。对于深度分页，建议使用时间范围过滤代替：

```sql
-- 不推荐：深分页
SELECT time, host, usage
FROM cpu
WHERE host = 'server-01'
ORDER BY time ASC
LIMIT 10 OFFSET 10000;

-- 推荐：基于时间的游标分页
SELECT time, host, usage
FROM cpu
WHERE host = 'server-01'
  AND time > 1713658000000  -- 上一页最后一条的时间戳
ORDER BY time ASC
LIMIT 10;
```

SonnetDB 同时支持两种分页风格，让熟悉不同 SQL 方言的用户都能快速上手。在大多数场景下，推荐使用游标分页以获得更好的性能表现。
