---
name: wal-recovery
description: SonnetDB WAL（预写日志）机制说明、崩溃恢复流程、数据目录完整性排查、常见损坏场景与修复步骤。
triggers:
  - wal
  - 崩溃
  - crash
  - 恢复
  - recovery
  - 数据损坏
  - corrupt
  - 目录
  - SDBWAL
  - SDBSEG
  - SDBCAT
  - replay
  - 日志
  - 启动失败
  - 打不开
  - 数据丢失
  - checkpoint
requires_tools:
  - query_sql
  - list_measurements
---

# WAL 与崩溃恢复指南

SonnetDB 使用 Append-only WAL（预写日志）保证写入持久性。理解 WAL 机制有助于排查启动失败、数据丢失和目录损坏问题。

---

## 1. 数据目录结构

```
<DataRoot>/
├─ .system/                      ← 控制面（用户/授权/安装信息）
│  ├─ installation.json
│  ├─ users.json
│  └─ grants.json
├─ metrics/                      ← 数据库目录（每个数据库一个）
│  ├─ catalog.SDBCAT             ← Series Catalog（XxHash64 ID 映射）
│  ├─ measurements.tslschema     ← Measurement Schema 定义
│  ├─ tombstones.tslmanifest     ← 删除墓碑清单
│  ├─ wal/
│  │  ├─ 0000000000000001.SDBWAL ← 分段 WAL（十六进制 LSN 命名）
│  │  ├─ 0000000000000042.SDBWAL
│  │  └─ active.SDBWAL           ← 旧格式（自动迁移）
│  └─ segments/
│     ├─ 0000000000000001.SDBSEG ← 不可变压缩数据段
│     └─ 0000000000000002.SDBSEG
└─ telemetry/                    ← 另一个数据库
```

**文件职责：**

| 文件 | 职责 | 丢失后果 |
|------|------|----------|
| `measurements.tslschema` | Schema 定义 | 无法识别列类型，启动失败 |
| `catalog.SDBCAT` | Series ID 映射 | 无法定位数据，查询返回空 |
| `tombstones.tslmanifest` | 删除记录 | 已删数据重新出现（幽灵数据） |
| `*.SDBWAL` | 未 flush 的写入 | 丢失最近写入（WAL 范围内） |
| `*.SDBSEG` | 已 flush 的历史数据 | 丢失对应时间段数据 |

---

## 2. WAL 工作原理

### 写入流程

```
INSERT SQL
    │
    ▼
WAL Append（Append-only + CRC 校验）
    │
    ▼
MemTable（内存）
    │
    ▼ 触发条件：
    │  - MemTableMaxPoints 达到阈值
    │  - flush=true/sync 参数
    │  - 服务器优雅关闭
    ▼
Flush → 生成不可变 .SDBSEG 文件
    │
    ▼
WAL Checkpoint（记录已 flush 的 LSN）
    │
    ▼
旧 WAL 文件可安全删除（自动管理）
```

### 崩溃恢复流程

```
服务器启动
    │
    ▼
读取 catalog.SDBCAT + measurements.tslschema
    │
    ▼
扫描 wal/ 目录，按 LSN 排序
    │
    ▼
从 Checkpoint LSN 开始 Replay WAL
（已 flush 的 LSN 之前的记录跳过）
    │
    ▼
重建 MemTable
    │
    ▼
服务就绪
```

**关键保证：**
- WAL 支持 CRC 校验，损坏的 WAL 记录会被截断（不会导致启动失败）
- WAL Replay 是幂等的，重复 Replay 不会产生重复数据
- 分段 WAL（PR #19）：每个 WAL 文件有独立的起始 LSN，便于定位和清理

---

## 3. 崩溃恢复场景

### 场景 A：正常崩溃（进程被杀/断电）

**症状：** 服务重启后，最近几秒/分钟的写入丢失。

**原因：** 写入已进 WAL 但尚未 flush 到 segment，WAL Replay 应能恢复。

**排查步骤：**
```bash
# 1. 检查 WAL 目录是否有未处理的 WAL 文件
ls -la <DataRoot>/<db>/wal/

# 2. 检查服务启动日志，确认 WAL Replay 是否成功
# 正常日志示例：
# [INFO] Replaying WAL from LSN 0x0000000000000042
# [INFO] WAL replay complete, 1024 records recovered

# 3. 启动后查询验证
SELECT count(*) FROM cpu WHERE time >= now() - 1h;
```

**如果数据仍然丢失：** 说明写入时未使用持久化模式，数据只在内存中。建议写入时使用 `flush=true`。

### 场景 B：WAL 文件损坏

**症状：** 启动时报错，包含 `CRC mismatch`、`WAL truncated`、`invalid record`。

**原因：** 磁盘错误、文件系统损坏、不完整写入。

**处理方式：**
- SonnetDB 会自动截断损坏位置之后的 WAL 记录（截断容忍机制）
- 损坏点之前的数据会被恢复，之后的数据丢失
- 如果整个 WAL 文件损坏，可手动删除该文件（丢失该文件范围内的未 flush 数据）

```bash
# 手动删除损坏的 WAL 文件（谨慎操作）
# 先停止服务
# 删除损坏文件
rm <DataRoot>/<db>/wal/0000000000000042.SDBWAL
# 重启服务
```

### 场景 C：Segment 文件损坏

**症状：** 查询特定时间范围时报错或返回异常数据。

**原因：** 磁盘错误导致 `.SDBSEG` 文件损坏。

**排查：**
```sql
-- 缩小时间范围定位损坏的 segment
SELECT count(*) FROM cpu WHERE time >= 1713676800000 AND time < 1713763200000;
SELECT count(*) FROM cpu WHERE time >= 1713763200000 AND time < 1713849600000;
```

**处理：**
- 损坏的 segment 对应时间范围的数据无法恢复（除非有备份）
- 可从备份恢复对应的 `.SDBSEG` 文件
- 删除损坏的 segment 文件后，该时间范围数据丢失但服务可正常运行

### 场景 D：catalog.SDBCAT 损坏

**症状：** 启动失败，或查询所有 measurement 返回空。

**原因：** Series Catalog 损坏，无法建立 series ID 到数据的映射。

**处理：**
- 这是最严重的损坏场景
- 如有备份，从备份恢复 `catalog.SDBCAT`
- 如无备份，需要重建 catalog（联系技术支持）

### 场景 E：旧格式 WAL 迁移

**症状：** 升级后发现 `active.SDBWAL` 文件，服务日志提示迁移。

**原因：** 旧版本使用单一 `active.SDBWAL`，新版本使用分段 WAL。

**处理：** SonnetDB 自动迁移，无需手动操作。迁移完成后 `active.SDBWAL` 会被重命名为带 LSN 的文件名。

---

## 4. 目录完整性检查

### 快速健康检查

```bash
# 检查数据库目录结构
ls -la <DataRoot>/<db>/
ls -la <DataRoot>/<db>/wal/
ls -la <DataRoot>/<db>/segments/

# 必须存在的文件
# ✅ catalog.SDBCAT
# ✅ measurements.tslschema
# ✅ tombstones.tslmanifest（可为空文件）
# ✅ wal/ 目录（可为空）
# ✅ segments/ 目录（可为空）
```

### 通过 HTTP 健康检查

```bash
# 服务级健康检查
curl http://127.0.0.1:5080/healthz

# 正常响应
{"status": "ok", "databases": ["metrics", "telemetry"]}

# 异常响应（某数据库加载失败）
{"status": "degraded", "errors": ["metrics: catalog load failed"]}
```

### 通过 SQL 验证数据完整性

```sql
-- 验证 schema 可读
SHOW MEASUREMENTS;
DESCRIBE MEASUREMENT cpu;

-- 验证数据可查（各时间段）
SELECT count(*) FROM cpu WHERE time >= now() - 1h;
SELECT count(*) FROM cpu WHERE time >= now() - 24h AND time < now() - 1h;
SELECT count(*) FROM cpu WHERE time >= now() - 7d AND time < now() - 24h;

-- 验证最新写入时间
SELECT time, host FROM cpu ORDER BY time DESC LIMIT 1;
```

---

## 5. 预防措施

### 写入持久化配置

```bash
# HTTP 批量写入时指定同步 flush
POST /v1/db/metrics/measurements/cpu/json?flush=true

# 或异步 flush（性能与持久性平衡）
POST /v1/db/metrics/measurements/cpu/json?flush=async
```

### 定期备份策略

```bash
# 停服备份（最安全）
# 1. 停止 SonnetDB 服务
# 2. 复制整个 DataRoot 目录
cp -r <DataRoot> <BackupPath>/sonnetdb-backup-$(date +%Y%m%d)
# 3. 重启服务

# 热备份（仅 segment 文件，WAL 中数据可能丢失）
# 先 flush 所有数据库
# 再复制 segments/ 目录
```

### MemTable 调优（减少数据丢失窗口）

```
# 降低 MemTableMaxPoints 可更频繁 flush，减少崩溃时丢失的数据量
# 但会增加 segment 数量和查询合并开销
# 推荐值：50000~200000（根据写入速率调整）
```

---

## 6. 常见问题

**Q: 服务启动后查询返回空，但数据目录有文件？**  
A: 检查 `catalog.SDBCAT` 是否存在且非空。如果 catalog 损坏，series 映射丢失，数据无法被找到。

**Q: WAL Replay 很慢，服务启动需要很长时间？**  
A: WAL 积累过多（长时间未 flush）。正常运行时应定期 flush。可临时调低 `MemTableMaxPoints` 触发更频繁的 flush。

**Q: 删除数据后重启，数据又出现了？**  
A: 检查 `tombstones.tslmanifest` 是否存在。DELETE 操作通过 tombstone 标记删除，如果 tombstone 文件丢失，已删数据会重新出现。

**Q: 升级 SonnetDB 版本后启动失败？**  
A: 检查 segment 文件格式版本兼容性。新版本引入了 `SegmentFormatVersion v3`（向量支持），旧版本 segment 文件向前兼容，但不能降级。
