---
name: retention-compaction
description: SonnetDB 数据保留（Retention）、删除（DELETE）、Tombstone 机制、Segment Compaction 行为说明与排查指南。
triggers:
  - retention
  - 保留策略
  - 数据过期
  - 删除
  - tombstone
  - compaction
  - 压缩
  - 合并
  - 磁盘占用
  - 数据清理
  - 过期数据
  - segment
  - flush
  - 存储增长
requires_tools:
  - query_sql
  - list_measurements
  - describe_measurement
---

# Retention、Tombstone 与 Compaction 指南

SonnetDB 的数据生命周期管理通过 DELETE + Tombstone 机制实现，Compaction 负责合并小 segment 提升查询效率。

---

## 1. 数据删除机制

### DELETE 语句

SonnetDB 的 DELETE 是**软删除**，通过 Tombstone 标记实现：

```sql
-- 删除特定 tag + 时间范围的数据
DELETE FROM cpu
WHERE host = 'server-01'
  AND time >= 1713676800000
  AND time <= 1713763200000;

-- 删除近 7 天的数据
DELETE FROM cpu
WHERE host = 'server-01'
  AND time >= now() - 7d
  AND time <= now();
```

**⚠️ 必须包含时间范围**，否则会删除该 tag 下所有时间点的数据（极危险操作）。

### Tombstone 工作原理

```
DELETE 执行
    │
    ▼
写入 WAL Delete 记录（PR #20）
    │
    ▼
追加到 tombstones.tslmanifest
    │
    ▼
查询时：合并 MemTable + Segments，过滤 Tombstone 范围
    │
    ▼
（后台）Compaction 时物理删除 Tombstone 覆盖的数据
```

**Tombstone 文件：** `<db>/tombstones.tslmanifest`
- 记录所有删除操作的时间范围和 series 信息
- 查询时实时过滤，性能影响与 tombstone 数量正相关
- Compaction 后 tombstone 对应的数据被物理删除，tombstone 条目可清理

---

## 2. 数据保留策略（Retention）

SonnetDB **当前版本没有自动 Retention Policy**（不同于 InfluxDB 的 RP 机制）。  
数据保留需要通过定期执行 DELETE 实现。

### 手动保留策略（推荐模式）

**方案 A：定期 DELETE 旧数据**

```sql
-- 保留最近 90 天，删除 90 天前的数据
DELETE FROM cpu
WHERE time < now() - 90d;

-- 按 tag 分批删除（减少单次操作影响）
DELETE FROM cpu WHERE host = 'server-01' AND time < now() - 90d;
DELETE FROM cpu WHERE host = 'server-02' AND time < now() - 90d;
```

**方案 B：通过 HTTP API 定期清理**

```bash
# 定时任务（cron）每天凌晨执行
curl -X POST "http://127.0.0.1:5080/v1/db/metrics/sql" \
  -H "Authorization: Bearer <admin-token>" \
  -H "Content-Type: application/json" \
  -d '{"sql": "DELETE FROM cpu WHERE time < now() - 90d"}'
```

**方案 C：ADO.NET 应用内定期清理**

```csharp
// 在后台服务中定期执行
public async Task CleanupOldDataAsync(SndbConnection conn, string measurement, TimeSpan retention)
{
    long cutoffMs = DateTimeOffset.UtcNow.Subtract(retention).ToUnixTimeMilliseconds();
    using var cmd = new SndbCommand(conn);
    cmd.CommandText = $"DELETE FROM {measurement} WHERE time < {cutoffMs}";
    await cmd.ExecuteNonQueryAsync();
}
```

### 不同数据类型的保留建议

| 数据类型 | 建议保留期 | 原因 |
|----------|-----------|------|
| 原始采样（秒级） | 7~30 天 | 数据量大，查询通常用聚合 |
| 分钟级聚合 | 90~180 天 | 中等粒度，保留较长 |
| 小时级聚合 | 1~3 年 | 趋势分析需要长历史 |
| 向量/嵌入 | 按业务需求 | 通常不过期 |
| 日志/事件 | 30~90 天 | 合规要求 |

---

## 3. Segment Compaction

### Compaction 的作用

```
写入路径：WAL → MemTable → Flush → 小 Segment
                                        │
                                        ▼ Compaction
                                    大 Segment（合并后）
```

**Compaction 的收益：**
- 减少 segment 文件数量，降低查询时的合并开销
- 物理删除 Tombstone 覆盖的数据，释放磁盘空间
- 重新压缩数据（Delta-of-Delta + Gorilla/XOR + RLE + 字典编码）

### Compaction 触发条件

| 触发条件 | 说明 |
|----------|------|
| Segment 数量超过阈值 | 自动后台 Compaction |
| 手动触发（后续版本） | 当前版本为自动管理 |
| 服务优雅关闭 | 可能触发最终 flush |

### 监控 Segment 数量

```sql
-- 通过慢查询判断 segment 是否过多
-- 如果查询很慢，用 ?explain=true 查看 segment 命中数
```

```bash
# 直接统计 segment 文件数量
ls <DataRoot>/<db>/segments/*.SDBSEG | wc -l
```

**Segment 数量参考：**
- `< 20`：正常
- `20~100`：可接受，查询性能略有影响
- `> 100`：建议检查 flush 频率，可能 MemTableMaxPoints 设置过小

---

## 4. 磁盘空间管理

### 磁盘占用分析

```bash
# 各组件磁盘占用
du -sh <DataRoot>/<db>/wal/        # WAL 文件
du -sh <DataRoot>/<db>/segments/   # Segment 文件（主要占用）
du -sh <DataRoot>/<db>/            # 数据库总占用
du -sh <DataRoot>/                 # 所有数据库总占用
```

### 磁盘增长过快的原因

| 原因 | 排查方法 | 解决方案 |
|------|----------|----------|
| 写入量大，未清理旧数据 | 检查 DELETE 是否定期执行 | 建立保留策略 |
| WAL 积累过多 | 检查 flush 是否正常触发 | 调低 MemTableMaxPoints |
| Compaction 未运行 | 检查 segment 文件数量 | 等待自动 Compaction |
| Tombstone 未被 Compaction 清理 | 检查 tombstones.tslmanifest 大小 | 等待 Compaction 完成 |

### 紧急释放磁盘空间

```sql
-- 1. 先估算可删除的数据量
SELECT count(*) FROM cpu WHERE time < now() - 90d;

-- 2. 执行删除（分批，避免长时间锁）
DELETE FROM cpu WHERE time < now() - 90d AND host = 'server-01';
DELETE FROM cpu WHERE time < now() - 90d AND host = 'server-02';
-- ... 继续其他 host

-- 3. 等待 Compaction 物理释放空间（后台自动进行）
```

---

## 5. 与其他数据库的对比

| 特性 | SonnetDB | InfluxDB | TimescaleDB | ClickHouse |
|------|----------|----------|-------------|------------|
| 自动 Retention Policy | ❌ 需手动 DELETE | ✅ RP 自动过期 | ✅ 分区自动 DROP | ✅ TTL 表达式 |
| 删除机制 | Tombstone + Compaction | 段级删除 | 分区 DROP | MergeTree 异步删除 |
| Compaction | 自动后台 | 自动 | 自动 | 自动 MergeTree |
| 删除后立即释放磁盘 | ❌ 等待 Compaction | ❌ 等待 Compaction | ✅ DROP PARTITION | ❌ 等待 Merge |

**迁移自 InfluxDB 的注意事项：**
- InfluxDB 的 Retention Policy 在 SonnetDB 中需要改为定期 DELETE
- InfluxDB 的 `DROP SERIES` 对应 SonnetDB 的 `DELETE FROM ... WHERE tag = ...`
- InfluxDB 的 `DROP MEASUREMENT` 在 SonnetDB 中同名，但语义相同

---

## 6. 常见问题

**Q: DELETE 执行后磁盘空间没有立即释放？**  
A: 正常现象。DELETE 是软删除（Tombstone），物理空间在 Compaction 后才释放。Compaction 是后台异步过程。

**Q: 删除数据后重启服务，数据又出现了？**  
A: 检查 `tombstones.tslmanifest` 是否存在且完整。如果 tombstone 文件损坏或丢失，已删数据会重新出现。

**Q: 查询时返回了应该被删除的数据？**  
A: 确认 DELETE 语句的时间范围是否正确（Unix 毫秒 vs 秒）。用 `SELECT count(*) WHERE time >= X AND time <= Y` 验证范围。

**Q: tombstones.tslmanifest 文件很大，影响性能？**  
A: 大量 tombstone 会增加查询时的过滤开销。等待 Compaction 完成后 tombstone 会被清理。如果 Compaction 长时间未运行，检查服务日志。

**Q: 如何确认 Compaction 是否在运行？**  
A: 观察 segment 文件数量是否在减少，以及 `/metrics` 端点中的 Compaction 相关指标（Milestone 17 后完整支持）。
