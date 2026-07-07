---
name: iot-schema-design
description: IoT / 工业场景下 SonnetDB 的标准 Schema 设计模式：多层 Measurement 结构、设备注册表、多级聚合表、四级告警阈值、设备在线状态检测、OEE 计算。
triggers:
  - iot
  - 物联网
  - 工业
  - 传感器
  - sensor
  - 设备
  - device
  - 工厂
  - 产线
  - 车间
  - plc
  - scada
  - opc
  - 告警阈值
  - 四级告警
  - 设备在线
  - 心跳
  - oee
  - 设备效率
  - 多级聚合
  - 降采样
requires_tools:
  - query_sql
  - describe_measurement
  - list_measurements
---

# IoT / 工业场景 Schema 设计指南

IoT 和工业场景是 SonnetDB 最典型的使用场景，本指南提供标准的多层 Schema 设计模式。

---

## 1. 推荐的四层 Schema 结构

```text
层级 1：设备注册表（静态元数据）
    └─ device_registry（设备型号、位置、规格等不变信息）

层级 2：原始采样数据（高频，短保留）
    └─ sensor_<类型>（每 1~30 秒采样，保留 7~30 天）

层级 3：分钟级聚合（中频，中保留）
    └─ sensor_<类型>_1m（每分钟聚合，保留 90~180 天）

层级 4：小时/天级聚合（低频，长保留）
    └─ sensor_<类型>_1h / sensor_<类型>_1d（长期趋势，保留 1~5 年）
```

---

## 2. 设备注册表（静态元数据）

设备的不变属性（型号、位置、规格）不适合放在时序 measurement 的 tag 中（会导致高基数），应单独存储：

```sql
-- 设备注册表：存储设备的静态属性
-- 每台设备只有一条记录（或极少更新）
CREATE MEASUREMENT device_registry (
    device_id    TAG,           -- 设备唯一标识，如 "PLC-A01"
    plant        TAG,           -- 工厂，如 "factory-hz"
    workshop     TAG,           -- 车间，如 "workshop-A"

    device_type  FIELD STRING,  -- 设备类型，如 "温度传感器"
    model        FIELD STRING,  -- 型号，如 "PT100-3A"
    manufacturer FIELD STRING,  -- 制造商
    install_date FIELD INT,     -- 安装时间（Unix 毫秒）
    rated_range_min FIELD FLOAT, -- 量程下限
    rated_range_max FIELD FLOAT  -- 量程上限
);

-- 写入设备信息（安装时执行一次）
INSERT INTO device_registry (time, device_id, plant, workshop,
    device_type, model, manufacturer, install_date,
    rated_range_min, rated_range_max)
VALUES (now(), 'PLC-A01', 'factory-hz', 'workshop-A',
    '温度传感器', 'PT100-3A', 'Siemens', now(),
    -50.0, 200.0);
```

---

## 3. 原始采样 Measurement

### 温湿度传感器

```sql
CREATE MEASUREMENT sensor_climate (
    device_id    TAG,           -- 设备 ID（关联 device_registry）
    workshop     TAG,           -- 车间（冗余存储，加速过滤）
    line         TAG,           -- 产线编号

    temp_celsius FIELD FLOAT,   -- 温度（摄氏度，精度 0.1）
    humidity_pct FIELD FLOAT,   -- 相对湿度（0~100%）
    pressure_pa  FIELD FLOAT,   -- 大气压（帕斯卡）
    quality_code FIELD INT      -- OPC-UA 质量码：192=好，0=坏，64=不确定
    -- time 列自动存在，Unix 毫秒
);
```

### 振动传感器

```sql
CREATE MEASUREMENT sensor_vibration (
    device_id    TAG,
    equipment    TAG,           -- 设备名，如 "motor-01"
    axis         TAG,           -- 轴向：x / y / z

    accel_ms2    FIELD FLOAT,   -- 加速度（m/s²）
    velocity_mms FIELD FLOAT,   -- 速度（mm/s，RMS）
    freq_hz      FIELD FLOAT,   -- 主频（Hz）
    quality_code FIELD INT      -- 质量码
);
```

### 电力监测

```sql
CREATE MEASUREMENT sensor_power (
    device_id    TAG,
    meter_id     TAG,           -- 电表编号
    phase        TAG,           -- 相位：A / B / C / total

    voltage_v    FIELD FLOAT,   -- 电压（伏）
    current_a    FIELD FLOAT,   -- 电流（安培）
    power_kw     FIELD FLOAT,   -- 有功功率（千瓦）
    energy_kwh   FIELD FLOAT,   -- 累计电量（千瓦时，单调递增）
    pf           FIELD FLOAT    -- 功率因数（0~1）
);
```

### 生产计数

```sql
CREATE MEASUREMENT sensor_production (
    device_id    TAG,
    line         TAG,           -- 产线
    product_code TAG,           -- 产品型号

    count_total  FIELD INT,     -- 累计产量（单调递增）
    count_ok     FIELD INT,     -- 合格品数
    count_ng     FIELD INT,     -- 不合格品数
    cycle_ms     FIELD FLOAT,   -- 节拍时间（毫秒）
    is_running   FIELD BOOL     -- 设备运行状态
);
```

---

## 4. 多级聚合表设计

原始数据保留期短，通过定期聚合写入粗粒度 measurement 实现长期存储：

```sql
-- 分钟级聚合（保留 180 天）
CREATE MEASUREMENT sensor_climate_1m (
    device_id    TAG,
    workshop     TAG,

    temp_avg     FIELD FLOAT,   -- 分钟平均温度
    temp_max     FIELD FLOAT,   -- 分钟最高温度
    temp_min     FIELD FLOAT,   -- 分钟最低温度
    humidity_avg FIELD FLOAT,   -- 分钟平均湿度
    sample_count FIELD INT,     -- 有效采样点数（验证完整性）
    bad_quality  FIELD INT      -- 质量码异常点数
);

-- 应用层定时聚合写入（每分钟执行）
-- 当前版本不支持在一条聚合 SQL 中同时按时间桶和 tag 维度分组。
-- 实践上建议应用层按 device_id/workshop 分批执行下面的模板，再写入 rollup 表：
-- SELECT
--        avg(temp_celsius), max(temp_celsius), min(temp_celsius),
--        avg(humidity_pct), count(*),
--        sum(CASE WHEN quality_code != 192 THEN 1 ELSE 0 END)
-- FROM sensor_climate
-- WHERE device_id = 'PLC-A01'
--   AND workshop = 'workshop-A'
--   AND time >= now() - 2m
--   AND time < now() - 1m
-- GROUP BY time(1m)
```

---

## 5. 四级告警阈值

工业场景标准的 HH/H/L/LL 四级告警：

```sql
-- 告警阈值配置表（静态，极少变更）
CREATE MEASUREMENT alarm_threshold (
    device_id    TAG,
    metric       TAG,           -- 指标名，如 "temp_celsius"

    hh_value     FIELD FLOAT,   -- 高高限（HH）：紧急停机
    h_value      FIELD FLOAT,   -- 高限（H）：告警
    l_value      FIELD FLOAT,   -- 低限（L）：告警
    ll_value     FIELD FLOAT,   -- 低低限（LL）：紧急停机
    deadband     FIELD FLOAT    -- 死区（防止频繁切换）
);

-- 写入温度告警阈值
INSERT INTO alarm_threshold (time, device_id, metric,
    hh_value, h_value, l_value, ll_value, deadband)
VALUES (now(), 'PLC-A01', 'temp_celsius',
    85.0, 75.0, 10.0, 0.0, 1.0);
```

### 告警状态查询

```sql
-- 查询当前超限的设备（最新一条数据与阈值对比）
-- 应用层结合 alarm_threshold 表实现
SELECT time, device_id, temp_celsius
FROM sensor_climate
WHERE workshop = 'workshop-A'
  AND time >= now() - 1m   -- 只看最新数据
  AND temp_celsius > 75.0  -- 超过高限（H）
ORDER BY time DESC;
```

---

## 6. 设备在线状态检测

```sql
-- 检测超过 5 分钟没有上报数据的设备（视为离线）
-- 方法：查询每个设备的最后上报时间，与当前时间对比

-- 当前版本不支持一条 SQL 直接按 device_id 聚合“每台设备最后心跳”。
-- 建议按单设备查询，或在写入侧额外维护 latest_status measurement。
SELECT max(time) AS last_seen
FROM sensor_climate
WHERE device_id = 'PLC-A01'
  AND time >= now() - 1h;

-- 应用层判断：last_seen < now() - 5m → 设备离线
```

### 心跳监控 Measurement

```sql
-- 专用心跳 measurement（设备每分钟上报一次）
CREATE MEASUREMENT device_heartbeat (
    device_id    TAG,
    workshop     TAG,

    is_alive     FIELD BOOL,    -- 固定为 true（有数据即在线）
    firmware_ver FIELD STRING,  -- 固件版本（便于版本管理）
    signal_rssi  FIELD INT      -- 无线信号强度（dBm，有线设备忽略）
);

-- 查询最近 10 分钟未上报心跳的设备
-- 应用层：对比 device_registry 中的所有设备 vs 最近心跳记录
```

---

## 7. OEE（设备综合效率）计算

OEE = 可用性（A）× 性能（P）× 质量（Q）

```sql
-- 计算某产线过去 8 小时的 OEE 分项
-- 基于 sensor_production measurement

SELECT
    -- 可用性 = 实际运行时间 / 计划运行时间
    -- 用 is_running=true 的采样点数 / 总采样点数近似
    avg(CASE WHEN is_running THEN 1.0 ELSE 0.0 END) AS availability,

    -- 性能 = 实际节拍 / 理论节拍（理论节拍 = 30000ms）
    avg(CASE WHEN cycle_ms > 0 THEN 30000.0 / cycle_ms ELSE 0.0 END) AS performance,

    -- 质量 = 合格品 / 总产量
    sum(count_ok) * 1.0 / NULLIF(sum(count_total), 0) AS quality,

    -- 总产量
    sum(count_total) AS total_count,
    sum(count_ok)    AS ok_count,
    sum(count_ng)    AS ng_count

FROM sensor_production
WHERE line = 'line-01'
  AND time >= now() - 8h
GROUP BY time(1h);
```

---

## 8. 数据保留策略建议

| 层级 | Measurement | 采样间隔 | 保留期 | 说明 |
| --- | --- | --- | --- | --- |
| 原始 | `sensor_*` | 1~30 秒 | 7~30 天 | 高频，磁盘占用大 |
| 分钟聚合 | `sensor_*_1m` | 1 分钟 | 90~180 天 | 中等，用于日常分析 |
| 小时聚合 | `sensor_*_1h` | 1 小时 | 1~3 年 | 趋势分析 |
| 天聚合 | `sensor_*_1d` | 1 天 | 永久 | 年度报表 |
| 告警事件 | `alarm_event` | 事件触发 | 5~10 年 | 合规要求 |
| 设备注册 | `device_registry` | 变更时 | 永久 | 设备台账 |

```sql
-- 定期清理原始数据（保留最近 30 天）
DELETE FROM sensor_climate WHERE time < now() - 30d;
DELETE FROM sensor_vibration WHERE time < now() - 30d;
```
