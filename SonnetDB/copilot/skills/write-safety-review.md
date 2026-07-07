---
name: write-safety-review
description: 在执行 SQL 前判断其是否为写入/删除/控制面危险操作，给出风险等级和审批建议。当用户要执行 DELETE、DROP、CREATE USER、GRANT、REVOKE、DROP DATABASE 等操作时触发。
triggers:
  - delete
  - drop
  - 删除
  - 清空
  - drop measurement
  - drop database
  - create user
  - drop user
  - grant
  - revoke
  - alter user
  - 危险操作
  - 写入审批
  - 权限变更
  - 数据清理
  - 批量删除
requires_tools:
  - query_sql
  - list_measurements
  - describe_measurement
---

# 写入安全审查指南

在 SonnetDB 中，部分 SQL 操作不可逆或影响范围广，执行前必须评估风险。

---

## 1. 操作风险分级

| 风险等级 | 颜色 | 操作类型 | 是否可逆 |
|----------|------|----------|----------|
| 🔴 **极高** | 红色 | DROP DATABASE、DROP MEASUREMENT、无时间范围的 DELETE | ❌ 不可逆 |
| 🟠 **高** | 橙色 | 有时间范围的 DELETE、DROP USER、REVOKE | ⚠️ 部分可恢复 |
| 🟡 **中** | 黄色 | CREATE USER、GRANT、ALTER USER、ISSUE TOKEN | ✅ 可撤销 |
| 🟢 **低** | 绿色 | INSERT、CREATE MEASUREMENT、SHOW/DESCRIBE | ✅ 安全 |

---

## 2. 极高风险操作（🔴 需要二次确认）

### DROP DATABASE

```sql
DROP DATABASE metrics;
```

**影响：** 删除整个数据库目录，包含所有 measurement、WAL、segments、catalog。  
**不可逆**，无回收站。

**审批流程：**
1. 确认数据库名称拼写正确
2. 确认已备份或数据不再需要
3. 确认没有其他服务正在连接该数据库
4. 在非高峰期执行

### DROP MEASUREMENT

```sql
DROP MEASUREMENT cpu;
```

**影响：** 删除该 measurement 的 schema 定义及所有历史数据。  
**不可逆**。

**执行前检查：**
```sql
-- 先查看数据量级
SELECT count(*) FROM cpu WHERE time >= now() - 30d;
-- 先查看 schema
DESCRIBE MEASUREMENT cpu;
```

### 无时间范围的 DELETE

```sql
-- ❌ 极危险：删除整个 measurement 的所有数据
DELETE FROM cpu WHERE host = 'server-01';
```

**风险：** 没有 time 范围限制时，会删除该 tag 下所有时间点的数据。

**正确做法：** 必须加时间范围：
```sql
DELETE FROM cpu
WHERE host = 'server-01'
  AND time >= 1713676800000
  AND time <= 1713763200000;
```

---

## 3. 高风险操作（🟠 需要确认范围）

### 有时间范围的 DELETE

```sql
DELETE FROM cpu
WHERE host = 'server-01'
  AND time >= now() - 7d
  AND time <= now();
```

**执行前检查清单：**
- [ ] 时间范围是否正确（`now() - 7d` 是否是预期的起点）
- [ ] tag 过滤条件是否精确（避免误删其他 host 的数据）
- [ ] 是否已确认数据不再需要（无法从 WAL 恢复已 flush 的数据）
- [ ] 是否在业务低峰期执行

**估算影响行数（执行前）：**
```sql
SELECT count(*) FROM cpu
WHERE host = 'server-01'
  AND time >= now() - 7d
  AND time <= now();
```

### DROP USER

```sql
DROP USER alice;
```

**影响：** 删除用户账号及其所有 token，该用户立即无法登录。  
**执行前确认：** 该用户是否有正在运行的服务或 CI/CD 任务使用其 token。

### REVOKE

```sql
REVOKE ON DATABASE metrics FROM alice;
```

**影响：** 立即生效，alice 的现有 token 对 metrics 数据库的访问权限被撤销。

---

## 4. 中风险操作（🟡 注意副作用）

### CREATE USER

```sql
CREATE USER alice WITH PASSWORD 'pa$$';
CREATE USER admin2 WITH PASSWORD 'secret' SUPERUSER;
```

**注意：**
- `SUPERUSER` 拥有所有数据库的 ADMIN 权限，谨慎授予
- 密码明文传输，确保使用 HTTPS 连接
- 新用户默认无任何数据库权限，需单独 GRANT

### GRANT

```sql
GRANT WRITE ON DATABASE metrics TO alice;
GRANT ADMIN ON DATABASE * TO admin2;  -- ⚠️ 通配符，影响所有数据库
```

**注意 `DATABASE *`：** 通配符授权影响当前和未来所有数据库，谨慎使用。

### ISSUE TOKEN

```sql
ISSUE TOKEN FOR alice;
```

**注意：** Token 只在颁发时显示一次，无法再次查看，请立即保存。

---

## 5. 控制面 SQL 的额外限制

控制面 SQL（`CREATE USER`、`GRANT`、`DROP DATABASE` 等）必须：
- 走 `POST /v1/sql` 端点（不是 `/v1/db/{db}/sql`）
- 使用 **admin** 角色的 token
- 非 admin token 调用会返回 `forbidden` 错误

**连接字符串示例（控制面操作）：**
```
Data Source=sonnetdb+http://127.0.0.1:5080;Token=<admin-token>
```

---

## 6. 安全审查 Checklist

执行任何写入/删除操作前，逐项确认：

```
□ 1. 操作的目标数据库/measurement 名称拼写正确
□ 2. DELETE 操作包含明确的时间范围
□ 3. 已用 SELECT count(*) 估算影响行数
□ 4. 已确认操作在业务低峰期执行
□ 5. DROP 操作已确认数据无需保留或已备份
□ 6. 控制面操作使用 admin token 且走 /v1/sql 端点
□ 7. GRANT ADMIN ON DATABASE * 已获得负责人审批
□ 8. 新建 SUPERUSER 已记录在访问控制文档中
```

---

## 7. 常见误操作与预防

| 误操作 | 后果 | 预防方法 |
|--------|------|----------|
| `DELETE FROM cpu WHERE host='x'`（无时间范围） | 删除该 host 所有数据 | 强制要求 time 范围 |
| `DROP MEASUREMENT` 误写 measurement 名 | 删除错误的 measurement | 先 `SHOW MEASUREMENTS` 确认 |
| `GRANT ADMIN ON DATABASE * TO alice` | alice 获得所有库的管理权 | 明确指定数据库名 |
| 在 `/v1/db/{db}/sql` 执行控制面 SQL | 返回 sql_error | 改用 `/v1/sql` |
| 用 readwrite token 执行 DROP | 返回 forbidden | 切换 admin token |
