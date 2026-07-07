## 多条件过滤：AND 连接多个 WHERE 约束

时序数据通常带有多个维度的标签（Tag）和时间戳，查询时往往需要组合多个条件才能精确定位目标数据。SonnetDB 的 `WHERE` 子句支持通过 `AND` 连接多个约束条件，实现 Tag 过滤、时间范围过滤和 Field 条件过滤的任意组合。

### 基本多条件查询

最常用的场景是组合 Tag 过滤和时间范围过滤，精确定位某个特定设备在特定时间段的数据：

```sql
SELECT time, host, region, usage
FROM cpu
WHERE host = 'server-01'
  AND region = 'cn-hz'
  AND time >= 1713657600000
  AND time < 1713657900000;
```

这条查询同时约束了三个维度：主机名必须是 `server-01`，地区必须是 `cn-hz`，时间范围在指定的毫秒时间戳区间内。通过 AND 连接的多个条件必须同时满足，结果集是各条件过滤后的交集。

### 混合 Tag 与 Field 过滤

除了 Tag 和时间，还可以加入 Field 值的条件，实现更精确的数据筛选：

```sql
-- 查找 CPU 使用率超过 70% 的高负载记录
SELECT time, host, usage, throttled
FROM cpu
WHERE host = 'server-01'
  AND usage > 0.7
  AND throttled = TRUE;

-- 查找特定标签组合且使用率在指定范围内的记录
SELECT time, host, usage, label
FROM cpu
WHERE region = 'cn-hz'
  AND usage >= 0.4
  AND usage <= 0.8
  AND cores = 8;
```

### 排除条件与多值匹配

当需要排除某些值时，可以结合 `!=` 或 `<>` 运算符。如果需要匹配多个值，可以使用 `IN` 操作符：

```sql
-- 排除特定主机
SELECT time, host, usage
FROM cpu
WHERE region = 'cn-hz'
  AND host != 'server-02';

-- 匹配多个主机（目前 IN 支持作为语法糖）
SELECT time, host, usage
FROM cpu
WHERE region = 'cn-hz'
  AND (host = 'server-01' OR host = 'server-03');
```

### 查询性能建议

多条件过滤的核心优势在于减少数据扫描量。SonnetDB 的存储引擎会利用 Tag 索引和时间范围来跳过不相关的数据块。以下是一些实用的性能建议：

- **Tag 条件放在前面**：将区分度高的 Tag 条件（如 `device_id`、`host`）放在 WHERE 子句的前面，有助于引擎快速缩小扫描范围。
- **时间范围是必选项**：对于包含大量历史数据的表，始终添加时间范围约束，避免全表扫描。即使你需要所有数据，也要明确写出时间范围。
- **使用 SELECT 预览再执行 DELETE**：在执行 DELETE 操作前，先用对应的 SELECT 确认影响范围：

```sql
-- 先预览
SELECT count(*) FROM cpu
WHERE host = 'server-01'
  AND time >= 1713658080000
  AND time <= 1713658140000;

-- 再删除
DELETE FROM cpu
WHERE host = 'server-01'
  AND time >= 1713658080000
  AND time <= 1713658140000;
```

多条件过滤是时序查询中最基础也最常用的技巧，掌握好 AND 组合的使用，能让你在海量时序数据中快速定位目标信息。
