## SonnetDB 数据保留策略：RetentionWorker、自动 Tombstone 与过期段清理

时序数据具有明显的时间衰减特性——新数据价值高，历史数据价值逐渐降低。长期积累的历史数据不仅占用大量存储空间，还会拖慢查询性能。SonnetDB 内置了完善的数据保留（Retention）机制，通过 **RetentionWorker**、**自动 Tombstone** 和 **过期段清理** 三个层次实现自动化数据生命周期管理。

### 配置保留策略

SonnetDB 的保留策略基于 TTL（Time To Live）概念，可以为每个 Measurement 单独设置数据保留时长：

```sql
-- 创建 measurement 时指定 TTL（数据保留 7 天）
CREATE MEASUREMENT cpu (
    host TAG,
    usage FIELD FLOAT,
    temperature FIELD FLOAT
) WITH (ttl = 604800000);

-- 或者为现有 measurement 修改 TTL
ALTER MEASUREMENT cpu SET TTL = 2592000000;  -- 30 天

-- 查看当前保留策略
SHOW RETENTION POLICIES;
```

TTL 以毫秒为单位指定数据保留时长。当数据的时间戳早于 `当前时间 - TTL` 时，该数据将被视为过期数据。SonnetDB 不会立即删除过期数据，而是通过后台的 RetentionWorker 异步处理。

### RetentionWorker 工作原理

RetentionWorker 是 SonnetDB 后台的一个长时间运行的守护任务，按照配置的时间间隔周期性扫描所有 Measurement 的过期数据：

```csharp
public class RetentionWorker : BackgroundService
{
    private readonly RetentionConfig _config;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // 按照配置的间隔执行检查
            await Task.Delay(
                TimeSpan.FromSeconds(_config.CheckIntervalSeconds), ct);

            foreach (var measurement in catalog.GetAllMeasurements())
            {
                // 跳过未设置 TTL 的 measurement
                if (!measurement.TtlMs.HasValue) continue;

                var expiredTime = DateTimeOffset.UtcNow
                    .ToUnixTimeMilliseconds() - measurement.TtlMs.Value;

                // 对过期数据应用 Tombstone
                await ApplyTombstoneAsync(measurement, expiredTime, ct);

                // 清理完全过期的段文件
                await DropExpiredSegmentsAsync(measurement, expiredTime, ct);
            }
        }
    }
}
```

RetentionWorker 的运行间隔通过 `SDB_RETENTION_CHECK_INTERVAL` 环境变量或配置文件控制，默认每 3600 秒（1 小时）执行一次。

### 自动 Tombstone 机制

SonnetDB 采用**标记删除（Tombstone）**策略来标记过期数据，而非立即从磁盘上擦除。这种设计避免了大规模删除操作对正在进行的读写请求的影响：

```csharp
public class TombstoneManager
{
    public async Task ApplyTombstoneAsync(
        Measurement measurement, long expiredBefore, CancellationToken ct)
    {
        // 为过期数据段创建 Tombstone 标记
        foreach (var segment in measurement.GetSegments())
        {
            if (segment.MaxTime <= expiredBefore)
            {
                // 段内所有数据都已过期 → 添加完整段 Tombstone
                await AddSegmentTombstoneAsync(segment);
            }
            else if (segment.MinTime <= expiredBefore)
            {
                // 段内部分数据过期 → 添加部分 Tombstone
                await AddPartialTombstoneAsync(segment, expiredBefore);
            }
        }
    }
}
```

Tombstone 文件以 `.tombstone` 后缀存储在段文件同名目录下。在被 Tombstone 标记的段上，查询时会自动过滤掉过期的时间行，确保返回给用户的数据都是有效的。

### 过期段清理（Space Reclamation）

当 RetentionWorker 确认一个段文件中的所有数据均已过期时，会触发物理删除操作，释放磁盘空间。清理过程同样采用安全的事务方式：

```csharp
private async Task DropExpiredSegmentsAsync(
    Measurement measurement, long expiredBefore, CancellationToken ct)
{
    foreach (var segment in measurement.GetSegments()
        .Where(s => s.MaxTime <= expiredBefore))
    {
        // 确认该段已有完整 Tombstone
        if (!segment.HasFullTombstone) continue;

        // 先写删除日志到 WAL，确保可恢复
        await wal.LogDeletionAsync(segment.SegmentId);

        // 执行物理删除
        File.Delete(segment.DataFilePath);
        File.Delete(segment.IndexFilePath);
        if (segment.TombstonePath != null)
            File.Delete(segment.TombstonePath);

        // 从 Catalog 中移除段记录
        catalog.RemoveSegment(measurement.Name, segment.SegmentId);
    }
}
```

### 保留策略配置示例

完整的保留策略配置可以通过环境变量或配置文件进行设置：

```yaml
# docker-compose 中配置保留策略
environment:
  - SDB_RETENTION_CHECK_INTERVAL=3600      # 检查间隔（秒）
  - SDB_RETENTION_DEFAULT_TTL=604800000     # 默认 TTL：7 天
  - SDB_RETENTION_AUTO_CREATE=true          # 自动创建保留策略
```

通过合理配置 TTL 保留策略，SonnetDB 可以自动管理数据生命周期：热数据保持在最佳性能区间、温数据按需保留、冷数据及时清理。这不仅降低了存储成本，也使数据库始终维持在最佳查询性能状态，无需人工介入进行数据清理和维护工作。
