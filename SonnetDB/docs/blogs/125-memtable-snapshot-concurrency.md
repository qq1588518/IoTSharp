# MemTable 优化：从热路径统计到快照并发

MemTable 是写入进入落盘 segment 之前的第一站。它既要承接高并发 append，也要服务查询和 flush 判断。最近的优化主要解决三个问题：统计热路径、字符串字段估算、读写并发。

## EstimatedBytes 不再全表扫描

Flush 决策依赖 `EstimatedBytes`、`MinTimestamp` 和 `MaxTimestamp`。过去如果每次 `ShouldFlush` 都遍历全部 series，数据量一大，flush 判断本身就会变成额外负担。

现在这些统计由生命周期增量维护：

- append 时更新估算大小和时间边界
- WAL replay 时恢复统计
- flush reset 时重置统计

这样 `ShouldFlush` 能保持轻量，不再在热路径扫描全部 series。

## 字符串字段估算前移

字符串字段的内存估算过去容易反复调用 `Encoding.UTF8.GetByteCount`。现在 `MemTableSeries.Append` 会增量计算字符串 UTF-8 byte count，并累加到 series 统计里。

测试覆盖了 string、null、非 string 混合场景，确保新的增量估算与旧逻辑一致。

## Snapshot 缓存

当 series 已排序且自上次快照后没有追加数据时，重复 `Snapshot` 或 range 查询会复用已排序快照。实现上避免暴露内部可变数组，对外仍保持只读语义。

这对 dashboard、短时间内重复查询同一 hot series 的场景很有帮助：数据没变，就不必重复排序和分配。

## SnapshotRange 只复制命中区间

范围查询过去可能先生成全量 snapshot，再二分裁剪。现在在已有有序数据上先二分 from/to，再只复制命中区间。

边界语义保持不变：

- 空范围返回空
- 单点范围可命中
- from/to 仍按既有 inclusive 规则处理

## 读写并发

`Snapshot` 和 `TryGetAggregate` 不应长期阻塞 append。新的实现思路采用 immutable snapshot swap：append 只让缓存失效，查询在需要时构造新的只读快照。

这不是把 MemTable 变成完全无锁结构，而是把锁持有时间压短，把昂贵工作尽量移出写入关键区。

MemTable 的这些变化没有改变外部行为，却降低了写入持续压力下的查询和 flush 判断成本。
