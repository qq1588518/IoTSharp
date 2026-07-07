## 案例：智能制造——汽车总装线传感器数据采集与 PID 控制

### 背景

某汽车零部件厂商在新建焊接总装线时，需要对产线上 200+ 台焊机、机械臂、传送带的运行数据进行实时采集、分析和控制反馈。传统方案是 PLC + 上位机组态软件，但数据孤岛严重，无法做跨机器的关联分析，且参数调整依赖现场工程师经验。

### 挑战

- **实时采集**：200 台设备，每台每秒上报 20 个指标，合计 4000 点/秒的持续写入
- **参数整定**：焊接温度、压力的 PID 控制参数需要根据不同工件批次动态调整
- **品质关联**：需要将设备运行参数与产品品质检测结果进行时序关联分析
- **故障预警**：提前发现设备异常，减少非计划停机

### 解决方案

工厂部署了一台边缘服务器运行 SonnetDB 服务端实例，所有设备通过 OPC UA 采集网关统一汇入，历史数据保留 2 年。

**焊接设备数据模型**：

```sql
CREATE MEASUREMENT welding_machine (
    machine_id    TAG,
    line_id       TAG,
    shift         TAG,
    temperature   FIELD FLOAT,
    pressure      FIELD FLOAT,
    current       FIELD FLOAT,
    weld_time     FIELD FLOAT,
    quality_score FIELD FLOAT   -- 视觉检测系统写入的品质分
);
```

**在线 PID 温度控制**：通过 `pid_series()` 实时计算每台焊机的温控输出量。

```sql
-- 焊机 WM-042 的实时温度 PID 控制输出（目标 850°C）
SELECT time, machine_id,
       temperature AS actual_temp,
       pid_series(850.0, temperature, time, 1.8, 0.3, 0.05) AS pid_output
FROM welding_machine
WHERE machine_id = 'WM-042'
  AND time > NOW() - INTERVAL '10m'
ORDER BY time;
```

**PID 参数自整定**：当更换工件批次时，基于历史阶跃响应数据自动整定新的 PID 参数。

```sql
-- IMC 方法：基于过去 2 小时数据估算最优 PID 参数
SELECT pid_estimate(850.0, temperature, time, 'imc') AS params
FROM welding_machine
WHERE machine_id = 'WM-042'
  AND time > NOW() - INTERVAL '2h';
-- 返回: {"kp": 2.1, "ki": 0.41, "kd": 0.08}
```

**品质-参数关联分析**：按时间桶聚合，发现高品质区间对应的参数范围。

```sql
-- 分析品质分 > 90 时的工艺参数特征
SELECT 
    avg(temperature)   AS avg_temp,
    avg(pressure)      AS avg_pressure,
    stddev(current)    AS current_stability,
    count(*)           AS sample_count,
    avg(quality_score) AS avg_quality
FROM welding_machine
WHERE shift = 'morning'
  AND time > NOW() - INTERVAL '7d'
GROUP BY time(1h)
HAVING avg(quality_score) > 90;
```

**多机横向对比**：找出产线上表现最差的机器。

```sql
-- 各焊机过去一周的品质均值排名
SELECT machine_id,
       avg(quality_score)  AS avg_quality,
       stddev(temperature) AS temp_stability,
       count(*)            AS weld_count
FROM welding_machine
WHERE time > NOW() - INTERVAL '7d'
GROUP BY machine_id
ORDER BY avg_quality ASC
LIMIT 10;
```

**振动异常预警**：提前发现机械臂轴承磨损。

```sql
-- 检测振动变点（CUSUM 算法），识别设备退化起始点
SELECT time, machine_id, vibration,
       changepoint(vibration, 'cusum', 5.0) AS is_changepoint
FROM welding_machine
WHERE time > NOW() - INTERVAL '24h'
  AND changepoint(vibration, 'cusum', 5.0) = 1;
```

**移动平均平滑监控曲线**：

```sql
-- 60 秒滑动平均消除传感器毛刺
SELECT time,
       temperature,
       moving_average(temperature, 60) AS smooth_temp
FROM welding_machine
WHERE machine_id = 'WM-042'
  AND time > NOW() - INTERVAL '30m';
```

### AI Copilot 辅助

工厂工程师通过 SonnetDB Web 界面中的 AI Copilot，用自然语言提问：

> "WM-042 昨天下午品质评分下跌，是什么原因？"

Copilot 自动生成关联分析 SQL，找出品质下跌前 30 分钟温度波动加剧、PID 输出超调的迹象，并建议重新整定 Ki 参数。

### 实施效果

| 指标 | 改造前 | 改造后 |
| --- | --- | --- |
| 参数调整周期 | 1-2 天（人工经验） | 10 分钟（自动整定） |
| 非计划停机次数/月 | 8 次 | 2 次 |
| 焊接品质合格率 | 94.2% | 97.8% |
| 数据分析时效 | T+1 天（导出 Excel） | 实时（SQL 查询） |
| 工艺调试人力 | 3 人/班 | 1 人/班 |

SonnetDB 的 PID 控制函数与时序分析能力的结合，让工厂工程师第一次能够用 SQL 回答"为什么昨天品质不好"这个问题。
