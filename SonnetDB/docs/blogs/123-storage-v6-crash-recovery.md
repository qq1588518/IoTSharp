# Segment v6 与崩溃恢复：把可靠性做到文件尾部

最近一次存储层升级的核心目标很朴素：当系统崩溃、断电或文件尾部损坏时，SonnetDB 应该能更快诊断问题，并尽量在不误读坏数据的前提下恢复可用状态。Segment v6 正是围绕这个目标设计的。

## 为什么要升级到 v6

在 v5 时代，为了不破坏主 `.SDBSEG` 格式，部分新能力以 sidecar 文件存在，例如 HNSW 向量索引 `.SDBVIDX` 和扩展聚合 sketch `.SDBAIDX`。这种方式兼容性好，但也带来两个问题：

- 文件组更多，部署、备份和 compaction 后清理逻辑更复杂。
- 主 segment 和 sidecar 之间需要额外一致性判断，尾部损坏时诊断线索有限。

既然这次已经升级到 v6，就把这些内容统一整合进主 segment。新段在 BlockIndex 之后、Footer 之前写入 extension section，用 section magic 区分 vector index 与 aggregate sketch。

## mini-footer 副本

Segment v6 还在 `SegmentHeader` 保留区写入 mini-footer 摘要副本。它不是第二份完整 Footer，而是关键索引定位信息：

- `IndexCount`
- `IndexOffset`
- `FileLength`
- `IndexCrc32`

如果文件尾部损坏，读取器可以根据 header 内的摘要判断“应有的 footer 长什么样”，给出更明确的诊断，并在满足约束时做受控 fallback。

## 兼容策略

升级格式不等于抛弃旧数据。当前读取层继续支持 v4/v5：

- v4/v5 仍按旧主格式读取。
- 旧 `.SDBVIDX` / `.SDBAIDX` sidecar 仍可按需加载。
- 新写入段使用 v6 内嵌 extension section。

这样做让现有数据库可以逐步通过 flush/compaction 自然迁移，而不是一次性离线转换。

## 测试重点

这轮测试覆盖了 v4/v5 兼容读取、v6 mini-footer round-trip、extension section 内嵌读取、legacy sidecar fallback，以及尾部损坏诊断。对存储格式来说，最重要的不是“新格式能读”，而是“坏格式不会被误读”。

Segment v6 的价值不在于多了一个版本号，而在于把分散的文件能力收回主格式，同时给恢复路径更多事实依据。
