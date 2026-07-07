## SonnetDB 状态分析函数：state_changes() 变化检测与 state_duration() 持续时间

在 IoT 和运维监控场景中，设备状态的变化和持续时间是最核心的分析维度之一。SonnetDB 提供了 `state_changes()` 和 `state_duration()` 两个窗口函数，专门用于分析离散状态或枚举型字段的变化模式。

### state_changes() 状态变化计数

`state_changes(field)` 逐行累计字段值的变化次数。首行输出 0；之后每当当前值与上一个非空值不相等时，计数加 1。该函数支持数值、布尔值和字符串三种类型：

```sql
SELECT time, status,
       state_changes(status) AS change_count
FROM device_monitor
WHERE device_id = 'pump-03'
ORDER BY time;
```

假设输入序列为 `online → online → offline → offline → online`，输出依次为 `0 → 0 → 1 → 1 → 2`。这个累计计数的斜率直接反映了状态切换的频率。

### state_duration() 状态持续时间

`state_duration(field)` 记录当前状态已持续的毫秒数。每当状态发生变化时，计时自动归零并从当前时间戳重新开始累计：

```sql
SELECT time, connection_status,
       state_duration(connection_status) AS duration_ms
FROM network_health
WHERE node = 'edge-07'
ORDER BY time;
```

如果 `connection_status` 在时间戳 `t0` 变为 `'disconnected'`，那么在 `t1` 行，`state_duration` 输出 `t1 - t0` 毫秒。直到状态再次变回 `'connected'`，计时重新开始。

### 实用场景：SLA 合规计算

组合两个函数可以轻松计算机器在线率等 SLA 指标：

```sql
SELECT time, status,
       state_changes(status) AS flaps,
       state_duration(status) AS current_state_ms
FROM uptime
WHERE host = 'critical-svc'
  AND time > NOW() - 24h;
```

通过分析 `flaps`（抖动次数）和 `current_state_ms`（当前状态持续时间），运维人员可以快速识别频繁状态切换的"抖动"设备。在高可用场景中，`state_duration` 可以直接用于计算连续运行时间。

### 高级用法：条件聚合

结合子查询和聚合函数，可以计算特定状态的累计时长：

```sql
SELECT MAX(state_duration(status)) AS max_uptime_ms
FROM (
  SELECT time, status,
         state_duration(status) AS dur
  FROM uptime WHERE host = 'web-svr'
) WHERE status = 'online';
```

SonnetDB 的状态分析函数为设备监控、SLA 审计和运维自动化提供了零代码的状态分析能力，使得复杂的状态追踪逻辑可以在纯 SQL 层完成，无需在应用层编写额外代码。
