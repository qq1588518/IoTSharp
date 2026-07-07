# Codec 专用化：BlockDecoder 为什么选择手写 fast path

最近的 codec 优化从一个问题开始：`ValuePayloadCodecV2`、`TimestampCodec` 和 `BlockDecoder` 是否应该通过 source generator、静态泛型或手写 fast path 做专用化？

结论是：先不要上 generator。当前最小、最稳的方案是手写 fast path。

## 旧路径的分配模型

V2 block 全量解码过去大致是：

1. delta-of-delta timestamp 解码到 `long[]`
2. 写入 `DataPoint[]`
3. value payload 解码到 `FieldValue[]`
4. 再写入 `DataPoint[]`

这个流程复用性强，但多了两个中间数组。对于重复查询和范围查询来说，这些分配会很快变成 GC 压力。

## 为什么不是 source generator

source generator 可以生成 Float64、Int64、Boolean、String 的专用代码，但它也引入了新的构建复杂度。SonnetDB.Core 要保持零运行时第三方依赖和 safe-only 原则，当前 codec 类型数量有限，手写分支更可控。

静态泛型也不是最自然的选择，因为 `FieldValue` 本身是 tagged union，落盘格式选择仍由 `FieldType` 和 `BlockEncoding` 决定。

## 新的 DecodeInto

`TimestampCodec` 现在可以把时间戳直接写入 `Span<DataPoint>`，值列保持默认值。

`ValuePayloadCodecV2` 新增：

- `DecodeInto`
- `DecodeRangeInto`

它们直接填充已有时间戳的 `DataPoint` 目标视图。`BlockDecoder` 因此不再需要中间 `long[]` / `FieldValue[]`。

## BenchmarkDotNet 结果

新增 `CodecSpecializationBenchmark`，用 16k 点的 V2 Float64 block 对比旧组合路径和生产快路径。

短跑结果：

| 场景 | Mean | Allocated |
| --- | ---: | ---: |
| V2 composable timestamp/value decode | 3.221 ms | 1984.38 KB |
| BlockDecoder.Decode V2 | 1.758 ms | 1024.13 KB |
| V2 composable range decode | 1.505 ms | 1024.51 KB |
| BlockDecoder.DecodeRange V2 | 1.951 ms | 128.30 KB |

短跑时间会有波动，但分配收益非常明确：全量解码分配约减半，range 解码分配大幅下降。

## 语义保护

测试覆盖 Float64、Int64、Boolean、String 的 V2 range 语义，确保新路径与 full decode 后切片一致。格式没有变化，旧 segment 和旧 V2 payload 都继续可读。

这次专用化的价值在于：把收益放在最热的地方，把复杂度控制在 codec 内部。
