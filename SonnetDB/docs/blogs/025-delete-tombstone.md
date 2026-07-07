## 删除数据：SonnetDB 的 DELETE 与 Tombstone 机制

时序数据的特点之一是"写多删少"，但数据清理仍然是必不可少的操作。SonnetDB 的 DELETE 语句采用 Tombstone（墓碑）机制而非直接物理删除，这一设计在保证写入性能的同时，也提供了数据恢复的可能性。

### DELETE 语法

SonnetDB 的 DELETE 语法与标准 SQL 类似，支持按时间范围或 Tag 条件删除数据：

```sql
-- 按时间范围删除
DELETE FROM cpu
WHERE host = 'server-01'
  AND time >= 1713658080000
  AND time <= 1713658140000;

-- 按 Tag 删除整条序列
DELETE FROM signal
WHERE source = 's-1';
```

删除操作是持久的，但建议在执行删除前先用对应的 SELECT 语句确认影响范围：

```sql
-- 先预览将要删除的数据
SELECT count(*) FROM cpu
WHERE host = 'server-01'
  AND time >= 1713658080000
  AND time <= 1713658140000;

-- 确认无误后再执行删除
DELETE FROM cpu
WHERE host = 'server-01'
  AND time >= 1713658080000
  AND time <= 1713658140000;
```

### Tombstone 机制的工作原理

与传统的"找到数据块、擦除、重写"的硬删除方式不同，SonnetDB 采用 Tombstone 机制：

1. **写入标记**：执行 DELETE 时，SonnetDB 不会修改已写入的 Segment 文件，而是在专门的 Tombstone 目录中写入一个标记文件，记录"哪些数据已被删除"。
2. **查询过滤**：在读取数据时，查询引擎会加载 Tombstone 信息，在返回结果前自动过滤掉已被标记删除的数据行。
3. **空间回收**：被标记删除的数据不会立即释放磁盘空间。在后台 Compaction（压缩合并）过程中，当 Segment 文件被合并重写时，Tombstone 标记的数据会被真正排除，从而回收存储空间。

### Tombstone 与硬删除的对比

| 特性 | Tombstone 机制 | 硬删除 |
|------|---------------|--------|
| 执行速度 | 极快（仅写元数据） | 慢（需读写数据文件） |
| 数据恢复 | 在 Compaction 前可恢复 | 不可恢复 |
| 空间释放 | 延迟释放（Compaction 时） | 立即释放 |
| 写入放大 | 无 | 高 |
| 并发影响 | 低 | 高 |

Tombstone 机制的核心理念是"先标记，后清理"。这在时序数据库中尤为重要，因为时序数据的写入通常是持续的高吞吐量流式写入，如果每次删除都需要重写数据文件，将严重影响写入性能。

### 管理 Tombstone 生命周期

SonnetDB 的 Compaction 管理器会自动处理 Tombstone 的消化。在 Compaction 过程中：

1. 读取旧的 Segment 文件
2. 加载对应的 Tombstone 信息
3. 排除被标记删除的数据行
4. 将剩余数据合并写入新的 Segment 文件
5. 删除旧的 Segment 文件和对应的 Tombstone

这个过程对用户是完全透明的，你不需要手动干预。SonnetDB 的 Compaction 使用 Size-Tiered 策略，会在后台按计划自动执行，确保在删除数据后存储空间最终被回收。

如果你需要立即释放空间（例如在测试环境中），可以触发手动 Compaction，不过在大多数生产环境中，让系统自动消化 Tombstone 就足够了。
