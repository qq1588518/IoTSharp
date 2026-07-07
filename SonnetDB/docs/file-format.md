---
layout: default
title: "文件格式与目录布局"
description: "SonnetDB 当前真实的磁盘布局，包括数据库目录、.system 控制面目录和帮助文档挂载位置。"
permalink: /file-format/
---

## 服务端数据根目录

`SonnetDB` 的数据根目录通常如下：

```text
<DataRoot>/
├─ .system/
├─ metrics/
├─ telemetry/
└─ ...
```

其中：

- `.system/` 用于服务端控制面
- 其余子目录各自代表一个数据库实例

## 数据库目录布局

每个数据库目录当前的真实布局如下：

```text
<database-root>/
├─ catalog.SDBCAT
├─ measurements.tslschema
├─ tombstones.tslmanifest
├─ wal/
│  └─ {startLsn:X16}.SDBWAL
└─ segments/
   └─ {id:X16}.SDBSEG
```

关键文件说明：

| 文件 | 作用 |
| --- | --- |
| `measurements.tslschema` | measurement schema 集合 |
| `catalog.SDBCAT` | series catalog |
| `tombstones.tslmanifest` | 删除与 retention 的 tombstone 清单 |
| `wal/*.SDBWAL` | 分段 WAL 文件 |
| `segments/*.SDBSEG` | 不可变数据段 |

## Segment v6 主文件布局

当前写入版本为 Segment Format v6。新段不再为 HNSW 向量索引或扩展聚合 sketch 生成独立 sidecar 文件，而是统一写入主 `.SDBSEG`：

```text
[SegmentHeader 64B]
[Block 0 ... Block N-1]
[BlockIndexEntry 0 ... N-1]
[Embedded Extension Area]
  ├─ SDBVIDX1 section（可选，HNSW VECTOR block index）
  └─ SDBAIDX1 section（可选，TDigest / HyperLogLog block sketch）
[SegmentFooter 64B]
```

v6 还会在 `SegmentHeader` 保留区写入一份 mini-footer 摘要（IndexCount / IndexOffset / FileLength / IndexCrc32）。当文件尾部 Footer 损坏或截断时，读取器可用这份摘要给出更明确的诊断，并在主 Footer 损坏但主段长度与索引区仍一致时恢复打开。

读取层继续兼容 v4/v5 主段。旧版本产生的 `.SDBVIDX` / `.SDBAIDX` 文件仍会作为 legacy fallback 按需读取，但新写出的 v6 段不会再创建这些额外文件。

## 与旧描述的差异

当前版本需要明确：

- 数据库存储不是单个 `.tsl` 文件
- schema、catalog、WAL、segments、tombstones 分别落在不同文件中
- 数据库的最小持久化单位是“数据库目录”

## WAL 兼容性说明

当前运行时使用分段 WAL：

```text
wal/{startLsn:X16}.SDBWAL
```

仓库中仍保留对旧 `wal/active.SDBWAL` 形态的兼容升级逻辑，旧目录在打开时会自动迁移到当前布局。

## `.system/` 控制面目录

服务端控制面数据保存在：

```text
<DataRoot>/.system/
├─ installation.json
├─ users.json
└─ grants.json
```

它们分别负责：

- `installation.json`：首次安装元数据，例如服务器 ID、组织、初始管理员和初始 token id
- `users.json`：用户、密码哈希、已签发 token 的摘要与哈希
- `grants.json`：数据库级授权

当 `.system/` 为空或尚未完成初始化时，访问 `/admin/` 会进入首次安装流程。

## 帮助文档在镜像中的位置

`docs/` 会在 Docker 构建阶段通过 JekyllNet 生成静态站点，并打包到镜像中的：

```text
wwwroot/help/
```

运行时通过 `/help/*` 对外提供帮助文档。

## 小结

如果你在排查启动、迁移、备份或容器挂载问题，请优先把注意力放在：

- `.system/`
- 各数据库子目录
- `measurements.tslschema`
- `catalog.SDBCAT`
- `wal/`
- `segments/`
- `tombstones.tslmanifest`
