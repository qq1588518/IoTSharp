## 安全机制详解：用户、角色与权限管理

数据库安全是企业级应用的核心关切。SonnetDB 提供了一套基于角色的访问控制（RBAC）体系，支持多用户管理、细粒度的权限分配和灵活的角色定义。本文将详细介绍 SonnetDB 的安全模型和操作方法。

### 三种预定义角色

SonnetDB 定义了三种内置角色，分别对应不同的操作权限级别。理解这三种角色是进行权限管理的基础。

**1. readonly（只读角色）**

只读角色拥有数据库中所有 Measurement 的 `SELECT` 权限，但不能执行任何写入、修改或管理操作。这个角色适用于数据分析人员、报表系统或需要查阅历史数据的应用程序。

**2. readwrite（读写角色）**

读写角色在只读权限的基础上，增加了数据写入和修改的权限。拥有此角色的用户可以执行 `INSERT`、`UPDATE`、`DELETE` 等数据操作语句。这是最常用的角色，适用于数据采集应用、ETL 管道等场景。

**3. admin（管理员角色）**

管理员角色拥有全部权限，包括：
- 数据操作权限（读写）
- 结构变更权限（`CREATE MEASUREMENT`、`ALTER`、`DROP`）
- 用户和令牌管理权限（`CREATE USER`、`ISSUE TOKEN`、`REVOKE TOKEN`）
- 系统管理权限（备份、恢复、配置修改）

管理员角色应仅授予需要执行管理操作的人员或系统组件。

### 用户管理

SonnetDB 使用标准的 `CREATE USER` 和 `DROP USER` SQL 语句进行用户管理：

```sql
-- 创建新用户并分配角色
CREATE USER 'analyst' WITH PASSWORD 'strong-password-123' ROLE readonly;

-- 创建读写用户
CREATE USER 'data-collector' WITH PASSWORD 'collector-pass-456' ROLE readwrite;

-- 创建管理员用户
CREATE USER 'db-admin' WITH PASSWORD 'admin-pass-789' ROLE admin;

-- 删除用户
DROP USER 'analyst';
```

创建用户时需要指定用户名、密码和角色。密码在存储时经过哈希处理，不会以明文形式保存。

### 使用 GRANT 和 REVOKE 管理权限

除了通过角色分配权限外，SonnetDB 还支持使用 `GRANT` 和 `REVOKE` 语句进行更细粒度的权限控制：

```sql
-- 授予特定用户对特定 measurement 的查询权限
GRANT SELECT ON MEASUREMENT cpu TO 'analyst';

-- 授予用户对特定 measurement 的写入权限
GRANT INSERT ON MEASUREMENT sensor_data TO 'data-collector';

-- 撤销权限
REVOKE SELECT ON MEASUREMENT cpu FROM 'analyst';
```

这种细粒度的权限控制方式，允许您在实际部署中遵循最小权限原则（Principle of Least Privilege），即每个用户只拥有完成其任务所必需的最小权限集合。

### 权限管理最佳实践

在实际应用中，建议遵循以下安全最佳实践：

1. **使用角色而非直接权限**：尽量通过角色来分配权限，而不是逐个授予。角色便于批量管理和维护。
2. **最小权限原则**：只为用户分配完成任务所必需的最小权限。例如，数据采集程序只需要 `readwrite` 角色，不需要 `admin` 权限。
3. **定期审计**：使用 `SHOW USERS` 命令查看所有用户及其角色，定期检查和清理不再需要的账户：

```sql
-- 查看所有用户
SHOW USERS;

-- 输出示例
-- username       | role
-- analyst        | readonly
-- data-collector | readwrite
-- db-admin       | admin
```

4. **强密码策略**：确保所有用户使用足够复杂的密码，避免使用默认密码或弱密码。
5. **安全传输**：在生产环境中，建议使用 HTTPS/TLS 加密客户端和服务器之间的通信，防止凭证在网络传输过程中被窃听。

### 与 Token 认证的协同

用户/角色体系和 Token 认证是 SonnetDB 安全体系的两个互补方面：
- **用户/角色** 用于管理"谁可以访问数据"——适用于需要区分多个操作者的场景。
- **Token 认证** 用于管理"什么应用可以访问系统"——适用于程序化访问场景。

在同一个 SonnetDB 实例中，您可以同时使用两种机制。用户在交互式查询时使用用户名和密码登录，而自动化脚本和微服务则使用 Bearer Token 进行身份验证。

通过这套完善的安全机制，SonnetDB 能够满足从个人项目到企业级应用的各种安全需求。正确的权限配置不仅能保护数据安全，也能避免误操作导致的数据损坏。
