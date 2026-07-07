---
name: auth-admin
description: SonnetDB 认证与授权完整指南：用户管理、Token 管理、角色权限、连接字符串配置、首次安装、常见权限错误排查。
triggers:
  - 认证
  - 授权
  - auth
  - token
  - 用户
  - user
  - 权限
  - permission
  - grant
  - revoke
  - 登录
  - login
  - unauthorized
  - forbidden
  - 403
  - 401
  - 连接字符串
  - bearer
  - admin
  - readonly
  - readwrite
  - 首次安装
  - setup
requires_tools:
  - query_sql
---

# 认证与授权管理指南

SonnetDB 使用 Bearer Token 认证，支持三种角色（readonly/readwrite/admin），通过控制面 SQL 管理用户和授权。

---

## 1. 角色与权限

| 角色 | 数据面读 | 数据面写 | 控制面 | 说明 |
|------|----------|----------|--------|------|
| `readonly` | ✅ | ❌ | ❌ | 只读访问指定数据库 |
| `readwrite` | ✅ | ✅ | ❌ | 读写指定数据库 |
| `admin` | ✅ | ✅ | ✅ | 全局管理员，可管理用户/授权/数据库 |

**权限端点对应：**

| 端点 | 需要角色 |
|------|----------|
| `POST /v1/db/{db}/sql`（SELECT） | readonly 及以上 |
| `POST /v1/db/{db}/sql`（INSERT/DELETE） | readwrite 及以上 |
| `POST /v1/sql`（控制面） | admin |
| `GET /healthz` | 无需认证 |
| `GET /metrics` | 无需认证（或配置保护） |
| `GET /admin/` | 无需认证（首次安装页面） |

---

## 2. 首次安装

### 通过 Admin UI

1. 访问 `http://127.0.0.1:5080/admin/`
2. 如果是首次安装，会显示初始化表单
3. 填写：组织名称、管理员用户名、管理员密码
4. 提交后生成初始 admin token（**只显示一次，立即保存**）

### 通过 API

```bash
# 检查是否需要初始化
GET /v1/setup/status
# 响应：{"initialized": false}

# 执行初始化
POST /v1/setup/initialize
Content-Type: application/json

{
  "organization": "MyOrg",
  "adminUsername": "admin",
  "adminPassword": "secure-password-here"
}

# 响应（保存 token！）
{
  "token": "tok_xxxxxxxxxxxxxxxx",
  "userId": "...",
  "message": "Installation complete"
}
```

**初始化完成后的文件：**
```
<DataRoot>/.system/
├─ installation.json   ← 服务器ID、组织、初始管理员信息
├─ users.json          ← 用户账号（密码哈希）+ token 摘要
└─ grants.json         ← 数据库级授权
```

---

## 3. 用户管理（控制面 SQL）

> ⚠️ 以下所有 SQL 必须走 `POST /v1/sql`，使用 admin token。

### 创建用户

```sql
-- 普通用户（无初始权限）
CREATE USER alice WITH PASSWORD 'pa$$word123';

-- 超级管理员（拥有所有数据库的 admin 权限）
CREATE USER admin2 WITH PASSWORD 'secure-pass' SUPERUSER;
```

**密码要求：** 建议至少 8 位，包含大小写字母和数字。

### 修改密码

```sql
ALTER USER alice WITH PASSWORD 'new-password-456';
```

### 删除用户

```sql
DROP USER alice;
```

**注意：** 删除用户会立即使其所有 token 失效。

### 查看用户列表

```sql
SHOW USERS;
```

---

## 4. 授权管理

### 授予权限

```sql
-- 授予读权限
GRANT READ ON DATABASE metrics TO alice;

-- 授予读写权限
GRANT WRITE ON DATABASE metrics TO alice;

-- 授予管理员权限（特定数据库）
GRANT ADMIN ON DATABASE metrics TO alice;

-- 授予所有数据库的管理员权限（⚠️ 谨慎）
GRANT ADMIN ON DATABASE * TO alice;
```

**权限层级：** `ADMIN` 包含 `WRITE`，`WRITE` 包含 `READ`。

### 撤销权限

```sql
-- 撤销所有权限（不指定具体权限级别）
REVOKE ON DATABASE metrics FROM alice;
```

### 查看授权

```sql
-- 查看所有授权
SHOW GRANTS;

-- 查看特定用户的授权
SHOW GRANTS FOR alice;
```

---

## 5. Token 管理

### 颁发 Token

```sql
-- 为用户颁发新 token
ISSUE TOKEN FOR alice;
```

**响应示例：**
```json
{
  "token": "tok_abcdef1234567890",
  "userId": "alice",
  "issuedAt": "2024-04-21T12:00:00Z"
}
```

**⚠️ Token 只在颁发时显示一次，无法再次查看，请立即保存。**

### 查看 Token 列表

```sql
-- 查看所有活跃 token（只显示摘要，不显示完整 token）
SHOW TOKENS;
```

### 撤销 Token

```sql
-- 撤销特定 token
REVOKE TOKEN 'tok_abcdef1234567890';
```

### 通过 HTTP API 登录获取 Token

```bash
POST /v1/auth/login
Content-Type: application/json

{
  "username": "alice",
  "password": "pa$$word123"
}

# 响应
{
  "token": "tok_xxxxxxxxxxxxxxxx",
  "expiresAt": null
}
```

---

## 6. 连接字符串配置

### 嵌入式模式（无认证）

```
Data Source=./demo-data
Data Source=sonnetdb://./demo-data
```

### 远程模式（Bearer Token）

```
Data Source=sonnetdb+http://127.0.0.1:5080/metrics;Token=tok_xxxxxxxxxxxxxxxx
Data Source=sonnetdb+https://db.example.com/metrics;Token=tok_xxxxxxxxxxxxxxxx
```

**连接字符串参数：**

| 参数 | 说明 | 示例 |
|------|------|------|
| `Data Source` | 服务器地址 + 数据库名 | `sonnetdb+http://127.0.0.1:5080/metrics` |
| `Token` | Bearer Token | `tok_xxxxxxxxxxxxxxxx` |

### C# ADO.NET 示例

```csharp
// 嵌入式
using var conn = new SndbConnection("Data Source=./demo-data");

// 远程（readwrite 用户）
using var conn = new SndbConnection(
    "Data Source=sonnetdb+http://127.0.0.1:5080/metrics;Token=tok_alice_token");

// 远程（admin 用户，用于控制面操作）
using var conn = new SndbConnection(
    "Data Source=sonnetdb+http://127.0.0.1:5080;Token=tok_admin_token");
```

### HTTP 请求认证

```bash
# 数据面查询
curl -X POST "http://127.0.0.1:5080/v1/db/metrics/sql" \
  -H "Authorization: Bearer tok_alice_token" \
  -H "Content-Type: application/json" \
  -d '{"sql": "SELECT * FROM cpu LIMIT 10"}'

# 控制面操作（需要 admin token）
curl -X POST "http://127.0.0.1:5080/v1/sql" \
  -H "Authorization: Bearer tok_admin_token" \
  -H "Content-Type: application/json" \
  -d '{"sql": "SHOW USERS"}'
```

---

## 7. 权限错误排查

### 常见错误码

| HTTP 状态 | 错误标识 | 原因 | 解决方案 |
|-----------|----------|------|----------|
| 401 | `unauthorized` | Token 缺失或无效 | 检查 Authorization 头，确认 token 正确 |
| 403 | `forbidden` | Token 有效但权限不足 | 检查用户角色，用 `SHOW GRANTS FOR user` 确认 |
| 404 | `db_not_found` | 数据库不存在 | 检查数据库名拼写，用 `SHOW DATABASES` 确认 |

### 排查流程

```bash
# 1. 确认服务正常
curl http://127.0.0.1:5080/healthz

# 2. 确认 token 有效（用 admin token 查看用户）
curl -X POST "http://127.0.0.1:5080/v1/sql" \
  -H "Authorization: Bearer <admin-token>" \
  -d '{"sql": "SHOW USERS"}'

# 3. 确认用户权限
curl -X POST "http://127.0.0.1:5080/v1/sql" \
  -H "Authorization: Bearer <admin-token>" \
  -d '{"sql": "SHOW GRANTS FOR alice"}'

# 4. 确认数据库存在
curl -X POST "http://127.0.0.1:5080/v1/sql" \
  -H "Authorization: Bearer <admin-token>" \
  -d '{"sql": "SHOW DATABASES"}'
```

### 典型错误场景

**场景 1：用控制面 SQL 走了数据面端点**
```
错误：POST /v1/db/metrics/sql 执行 CREATE USER → sql_error
解决：改用 POST /v1/sql（控制面端点）
```

**场景 2：readwrite 用户尝试执行 DROP DATABASE**
```
错误：403 forbidden
解决：DROP DATABASE 需要 admin 角色，切换 admin token
```

**场景 3：Token 被撤销后仍在使用**
```
错误：401 unauthorized
解决：重新登录获取新 token：POST /v1/auth/login
```

**场景 4：新建用户忘记 GRANT 权限**
```
错误：403 forbidden（用户存在但无权限）
解决：GRANT READ/WRITE ON DATABASE metrics TO alice
```

---

## 8. 安全最佳实践

```
✅ 为每个服务/应用创建独立用户，不共用 token
✅ 只授予最小必要权限（readonly 优先）
✅ 定期轮换 token（REVOKE 旧 token，ISSUE 新 token）
✅ admin token 只用于管理操作，不嵌入应用代码
✅ 生产环境使用 HTTPS（sonnetdb+https://）
✅ 将 token 存储在环境变量或密钥管理服务中，不硬编码
❌ 不要使用 GRANT ADMIN ON DATABASE * 给普通用户
❌ 不要在日志中打印完整 token
❌ 不要在 URL 参数中传递 token（用 Authorization 头）
```
