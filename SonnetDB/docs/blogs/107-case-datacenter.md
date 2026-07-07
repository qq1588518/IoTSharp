## 案例：数据中心——服务器集群的指标采集与容量预测

### 背景

某云服务商运营着 3 个数据中心，共 8000 台物理服务器，每台服务器运行 10-50 个虚拟机或容器。运维团队面临的核心挑战是：如何在不过度采购硬件的前提下，保证服务 SLA，同时提前预警容量瓶颈，避免"突然爆满"导致的服务降级。

### 挑战

- **指标体量**：8000 台物理机 × 平均 30 个指标 × 每 15 秒采集，峰值 1600 万点/分钟
- **多层次分析**：需要从物理机→虚拟机→容器→应用的多层次关联分析
- **容量预测**：需要提前 2-4 周预测 CPU、内存、磁盘的使用趋势，指导采购计划
- **告警疲劳**：传统阈值告警误报率高，运维团队每天处理 200+ 条告警，大部分是噪音

### 解决方案

**数据模型**：

```sql
CREATE MEASUREMENT server_metrics (
    host        TAG,
    datacenter  TAG,
    rack        TAG,
    role        TAG,   -- web / db / cache / compute
    cpu_pct     FIELD FLOAT,
    mem_pct     FIELD FLOAT,
    disk_pct    FIELD FLOAT,
    net_in_mbps FIELD FLOAT,
    net_out_mbps FIELD FLOAT,
    load_avg_1m FIELD FLOAT,
    iops        FIELD INT
);
```

**实时集群健康概览**：

```sql
-- 各数据中心当前资源使用率（P95）
SELECT datacenter,
       percentile(cpu_pct, 95)  AS p95_cpu,
       percentile(mem_pct, 95)  AS p95_mem,
       percentile(disk_pct, 95) AS p95_disk,
       count(DISTINCT host)     AS host_count,
       count(*) FILTER (WHERE cpu_pct > 90) AS cpu_critical_count
FROM server_metrics
WHERE time > NOW() - INTERVAL '5m'
GROUP BY datacenter;
```

**智能告警——基于 MAD 的动态阈值**：相比固定阈值，MAD 方法能自适应每台服务器的正常波动范围，大幅减少误报。

```sql
-- 检测 CPU 使用率异常（MAD 方法，阈值 3.5 倍）
SELECT host, datacenter, cpu_pct,
       anomaly(cpu_pct, 'mad', 3.5) AS is_anomaly
FROM server_metrics
WHERE time > NOW() - INTERVAL '30m'
  AND anomaly(cpu_pct, 'mad', 3.5) = 1
ORDER BY cpu_pct DESC;
```

**容量趋势预测**：提前 4 周预测磁盘使用率，指导扩容计划。

```sql
-- 预测 DB 集群磁盘使用率未来 4 周趋势
SELECT *
FROM forecast(
    SELECT avg(disk_pct) AS avg_disk
    FROM server_metrics
    WHERE role = 'db'
      AND datacenter = 'DC-BJ'
    GROUP BY time(1d),
    28,
    'holt_winters'
);
```

**资源使用率变化率**：检测 CPU 使用率快速上升（可能是流量突增或内存泄漏）。

```sql
-- 过去 10 分钟 CPU 使用率上升超过 20% 的主机
SELECT host, role,
       first(cpu_pct) AS cpu_10min_ago,
       last(cpu_pct)  AS cpu_now,
       last(cpu_pct) - first(cpu_pct) AS cpu_increase
FROM server_metrics
WHERE time > NOW() - INTERVAL '10m'
GROUP BY host, role
HAVING last(cpu_pct) - first(cpu_pct) > 20
ORDER BY cpu_increase DESC;
```

**周期性模式分析**：识别每周/每日的流量规律，用于弹性伸缩策略。

```sql
-- 分析过去 4 周各小时的 CPU 使用率规律（用于制定弹性伸缩计划）
SELECT strftime('%w', time) AS day_of_week,
       strftime('%H', time) AS hour_of_day,
       avg(cpu_pct)         AS avg_cpu,
       percentile(cpu_pct, 90) AS p90_cpu
FROM server_metrics
WHERE role = 'web'
  AND datacenter = 'DC-BJ'
  AND time > NOW() - INTERVAL '28d'
GROUP BY day_of_week, hour_of_day
ORDER BY day_of_week, hour_of_day;
```

**慢查询关联分析**：将数据库服务器的 CPU 高峰与慢查询日志关联。

```sql
-- 找出 DB 服务器 CPU > 80% 时段，关联慢查询数量
SELECT a.time,
       a.host,
       a.cpu_pct,
       b.slow_query_count
FROM server_metrics a
JOIN (
    SELECT time, host, count(*) AS slow_query_count
    FROM slow_query_log
    WHERE query_time_ms > 1000
    GROUP BY time(1m), host
) b ON a.host = b.host AND a.time = b.time
WHERE a.cpu_pct > 80
  AND a.role = 'db'
  AND a.time > NOW() - INTERVAL '24h'
ORDER BY a.cpu_pct DESC;
```

**Holt-Winters 平滑监控曲线**：消除周期性波动，更清晰地看到趋势。

```sql
-- 过去 7 天 CPU 使用率的双指数平滑曲线
SELECT time, cpu_pct,
       holt_winters(cpu_pct, 0.3, 0.1) AS smoothed_cpu
FROM server_metrics
WHERE host = 'db-primary-01'
  AND time > NOW() - INTERVAL '7d';
```

**容量规划报告**：

```sql
-- 各角色服务器的容量使用趋势（月度汇总）
SELECT role,
       strftime('%Y-%m', time) AS month,
       avg(cpu_pct)            AS avg_cpu,
       avg(mem_pct)            AS avg_mem,
       max(disk_pct)           AS max_disk,
       percentile(cpu_pct, 99) AS p99_cpu
FROM server_metrics
WHERE time > NOW() - INTERVAL '6m'
GROUP BY role, month
ORDER BY role, month;
```

### AI Copilot 辅助运维

运维工程师通过 Copilot 提问：

> "DC-BJ 的 DB 集群磁盘按当前增速，什么时候会满？"

Copilot 自动生成预测 SQL，计算出约 47 天后磁盘使用率将超过 85% 的警戒线，并建议提前 3 周启动扩容流程。

### 实施效果

| 指标 | 上线前 | 上线后 |
| --- | --- | --- |
| 每日告警数量 | 200+ 条 | 15-20 条（↓90%） |
| 告警误报率 | 65% | 8% |
| 容量预警提前量 | 3-5 天（人工判断） | 3-4 周（自动预测） |
| 非计划扩容次数/季度 | 4-6 次 | 0-1 次 |
| 运维人员处理告警时间/天 | 3 小时 | 20 分钟 |

SonnetDB 的 MAD 动态告警阈值将告警噪音降低了 90%，让运维团队从"告警疲劳"中解放出来，专注于真正需要处理的问题。
