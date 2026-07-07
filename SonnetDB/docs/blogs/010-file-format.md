## 深入探讨：SonnetDB 的文件格式与存储布局

理解数据库在磁盘上的文件组织方式，对于性能调优、数据备份和故障恢复都至关重要。SonnetDB 采用了一套精心设计的文件格式体系，在保证读写性能的同时，兼顾了数据管理的便捷性。本文将详细介绍 SonnetDB 的文件目录结构和各文件的作用。

### 数据目录结构

SonnetDB 的数据存储在配置的根目录下（默认为 `/data` 或 `./data`）。一个典型的数据目录结构如下：

```
/data/
  ├── catalog.SDBCAT              # 全局目录文件
  ├── measurements/
  │   ├── cpu/                    # "cpu" 测量的数据目录
  │   │   ├── CPU.tslschema       # 测量的 Schema 定义
  │   │   ├── wal/                # WAL（预写日志）目录
  │   │   │   └── wal_000001.log  # WAL 段文件
  │   │   ├── segments/           # 持久化段文件
  │   │   │   ├── seg_000001.data # 数据段文件
  │   │   │   ├── seg_000002.data
  │   │   │   └── ...
  │   │   └── index/              # 索引文件
  │   │       └── hnsw_001.idx    # HNSW 向量索引文件
  │   └── temperature/
  │       ├── temperature.tslschema
  │       ├── wal/
  │       └── segments/
  └── system/                     # 系统内部数据
      └── meta.sdbmeta            # 系统元数据
```

### 全局目录文件（catalog.SDBCAT）

`catalog.SDBCAT` 是数据库的全局目录文件，记录了所有 Measurement 的元信息，包括每个 Measurement 的名称、文件路径、创建时间等。这个文件是数据库启动时首先读取的文件，用于构建内存中的 Database Catalog 结构。

由于该文件的重要性，SonnetDB 在写入 catalog 信息时采用了原子写入机制，确保即使在写入过程中发生崩溃，也不会导致目录信息损坏。

### Schema 定义文件（*.tslschema）

每个 Measurement 都有一个对应的 schema 文件，命名格式为 `{MeasurementName}.tslschema`。这个文件以二进制格式存储了该 Measurement 的结构定义，包括：

- Measurement 名称
- 所有 Tag 列的名称和顺序
- 所有 Field 列的名称、数据类型
- 任何特殊列（如 VECTOR、GEOPOINT）的定义
- 索引配置信息（如 HNSW 参数）

Schema 文件在 `CREATE MEASUREMENT` 执行时创建，之后只读不写。如果需要修改结构，需要通过特定的 `ALTER` 指令处理。

### WAL 文件（预写日志）

WAL 文件位于每个 Measurement 的 `wal/` 子目录中。WAL 采用顺序追加的日志格式，每条日志记录包含：
- 日志序列号（LSN）
- 操作类型（INSERT、DELETE 等）
- 数据载荷（序列化的行数据）
- 校验和（用于检测数据损坏）

WAL 文件会按照大小进行切分，当单个 WAL 文件达到阈值（默认 64MB）时，会自动创建新的 WAL 段。旧的 WAL 段在对应数据已成功刷写到 Segment 后会被清理。

### Segment 数据文件

Segment 文件是 SonnetDB 的主要数据存储文件，位于 `segments/` 目录中。Segment 采用列式存储格式，相同列的数据在文件中连续存放，这不仅有利于压缩，还能在查询时只读取需要的列，减少 I/O 开销。

每个 Segment 文件包含：
- **文件头**：包含 Segment 元信息（创建时间、行数、时间范围等）
- **列数据块**：按列组织的压缩数据块
- **索引区域**：各列的稀疏索引和 Bloom Filter
- **文件尾**：包含各数据块在文件中的偏移量

### 向量索引文件

当 Measurement 包含 VECTOR 类型的列并配置了 HNSW 索引时，相应的索引文件会存储在 `index/` 目录中。索引文件的命名格式为 `{index_type}_{id}.idx`。

### 文件管理与维护

了解文件布局后，以下操作场景会更加得心应手：

- **数据备份**：直接复制整个数据目录，或者在数据库运行时使用 `sndb export` 命令进行逻辑备份。
- **空间回收**：SonnetDB 的 Compaction 机制会自动合并 Segment 文件并清理过期数据。也可以手动执行 `sndb compact` 触发压缩。
- **故障恢复**：如果数据文件损坏，可以尝试从 WAL 文件中进行恢复。WAL 文件通常包含了最新的写入数据。

SonnetDB 的文件格式设计贯穿了"简单可靠、性能优先"的原则。理解这些文件的组织方式，将帮助您在实际运维中更加得心应手。
