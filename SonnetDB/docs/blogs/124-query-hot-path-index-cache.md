# 查询热路径优化：索引、缓存与少一点 LINQ

时序数据库的查询性能并不只取决于压缩算法。很多时候，真正影响延迟的是那些每次查询都会做的小动作：构建字典、扫描 block、过滤 tombstone、解码相同 payload。最近的 QueryEngine 和 SegmentReader 优化，就是把这些动作尽量变成“一次构建，多次复用”。

## SegmentReader 的只读索引

`SegmentReader.Open` 现在会为 block 构建两个内存索引：

- `SeriesId -> BlockDescriptor[]`
- 按时间范围查找 block 的索引

它们只存在内存里，不改变 segment 文件格式。`FindBySeries`、`FindBySeriesAndField` 和 `FindByTimeRange` 可以直接命中候选集合，而不是每次线性扫描全部 block。

返回顺序仍保持稳定：多 series、多 field、乱序写入后的 segment 内顺序，以及时间区间重叠场景都按原语义处理。

## Reader map 快照缓存

`QueryEngine.BuildReaderMap` 过去会在查询期间反复构建 `SegmentId -> SegmentReader` 映射。现在它和 `SegmentManager` 发布的 Readers/Index 快照绑定：snapshot 没变，map 就可以复用。

这有一个关键边界：不能在 `AddSegment`、`SwapSegments` 或 `Dispose` 后继续使用过期 reader。因此 segment manager 每次变更都会发布新快照，compaction swap 会延迟释放仍被查询租约持有的旧 reader。

## Tombstone 过滤热路径

点查询中的 tombstone 过滤去掉了 LINQ `Where`，改为手写迭代器，并先按查询时间窗筛掉不可能命中的 tombstone。输出顺序、Limit 语义和 tombstone 的闭区间覆盖语义保持不变。

这类改动看起来不华丽，但很适合热路径：少一层闭包、少一个枚举器、少一次不必要的 tombstone 判断，就能在高频查询里持续省下成本。

## Block 解码缓存

SegmentReader 增加了解码后的 block 缓存，key 至少包含：

- `SegmentId`
- `BlockIndex`
- block `Crc32`

这保证 compaction 或文件替换后不会错误复用旧 payload。缓存受内存预算和 LRU 淘汰控制，reader dispose 时释放引用。

## mmap 回退路径

大 segment 可以选择 memory-mapped 读取，减少 `File.ReadAllBytes` 带来的 LOH 压力。默认 byte[] reader 仍保留，mmap 打开失败会自动回退。

这一组优化的共同点是：不改变 SQL 输出，不改变文件格式，只把查询前后那些重复的小成本收拢起来。
