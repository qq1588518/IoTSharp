## Token 认证机制：使用 ISSUE TOKEN 保障 API 安全

在微服务和 API 驱动的应用架构中，传统的用户名+密码认证方式已不再是最佳选择。SonnetDB 提供了基于 Token 的无状态认证机制，通过 `ISSUE TOKEN` 和 `REVOKE TOKEN` 两条核心 SQL 语句，实现了安全、灵活且易于管理的 API 访问控制。本文将详细介绍这套机制的使用方法。

### 什么是 Bearer Token

Bearer Token（持有者令牌）是一种广泛应用于 RESTful API 的身份验证方式。其工作流程如下：

1. 客户端（应用程序）向 SonnetDB 请求一个令牌。
2. SonnetDB 生成一个包含权限信息的加密令牌字符串并返回给客户端。
3. 客户端在后续的每个 API 请求中，将令牌放在 HTTP 请求头的 `Authorization` 字段中发送。
4. SonnetDB 验证令牌的有效性和权限，决定是否允许该操作。

Bearer Token 的优势在于无状态——服务器不需要存储会话信息，令牌本身包含了所有必要的验证数据。这在水平扩展和微服务部署中极具优势。

### 签发 Token

在 SonnetDB 中，使用 `ISSUE TOKEN` SQL 语句来生成令牌：

```sql
-- 签发一个具有管理员权限的令牌
ISSUE TOKEN 'my-app-token' WITH ROLE admin;

-- 签发一个具有读写权限、带过期时间的令牌
ISSUE TOKEN 'data-pipeline-token' WITH ROLE readwrite EXPIRATION '2025-12-31';

-- 签发一个只读令牌用于报表系统
ISSUE TOKEN 'reporting-token' WITH ROLE readonly;
```

执行成功后，SonnetDB 会返回一个令牌字符串，格式类似于：

```
sndb_v1_eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ0b2tlbl9pZCI6IjEyMyIsInJvbGUiOiJhZG1pbiJ9.signature
```

**重要提示**：令牌字符串只在签发时显示一次。请务必立即将其复制并安全保存（例如密码管理器或环境变量配置文件）。如果丢失，您无法再次获取原始令牌，只能撤销旧令牌并重新签发。

### 使用 Token 访问 API

获取令牌后，在所有的 HTTP API 请求中将其加入请求头：

```bash
# 使用 curl 通过 Token 访问 API
curl -H "Authorization: Bearer sndb_v1_eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ0b2tlbl9pZCI6IjEyMyIsInJvbGUiOiJhZG1pbiJ9.signature" \
  -H "Content-Type: application/json" \
  -X POST \
  -d '{"query": "SELECT * FROM cpu LIMIT 10"}' \
  http://localhost:8839/api/v1/query
```

在编程语言中使用：

```csharp
// C# 示例
using var client = new HttpClient();
client.DefaultRequestHeaders.Authorization = 
    new AuthenticationHeaderValue("Bearer", token);
var response = await client.PostAsJsonAsync("http://localhost:8839/api/v1/query", 
    new { query = "SELECT * FROM cpu LIMIT 10" });
```

```python
# Python 示例
import requests
headers = {"Authorization": f"Bearer {token}"}
response = requests.post(
    "http://localhost:8839/api/v1/query",
    json={"query": "SELECT * FROM cpu LIMIT 10"},
    headers=headers
)
```

### 撤销 Token

当令牌泄露、员工离职或者需要轮换凭证时，可以使用 `REVOKE TOKEN` 立即吊销令牌：

```sql
-- 根据令牌名称撤销
REVOKE TOKEN 'my-app-token';

-- 撤销所有令牌（谨慎使用）
REVOKE ALL TOKENS;
```

令牌被撤销后，任何使用该令牌的请求都将被拒绝，返回 HTTP 401 Unauthorized 状态码。令牌的吊销是即时生效的，SonnetDB 会更新内存中的令牌黑名单，并在下一次请求时进行检查。

### Token 管理最佳实践

1. **命名约定**：为令牌使用有意义的名称，如 `ci-cd-pipeline-v2`、`production-reader`、`dev-environment`，方便审计和管理。
2. **设置过期时间**：为临时任务或测试环境签发带过期时间的令牌，避免造成永久的未使用令牌积累。
3. **定期轮换**：对于生产环境，建议每 90 天轮换一次令牌。先签发新令牌，更新应用程序配置，再撤销旧令牌。
4. **最小权限原则**：只为令牌分配完成任务所需的最小角色。一个只做数据写入的应用不应该拥有 admin 或 readonly 权限。
5. **安全存储**：不要将令牌硬编码在源代码中。使用环境变量、密钥管理服务（如 Azure Key Vault、AWS Secrets Manager）或 Kubernetes Secrets。

```bash
# 推荐：从环境变量读取令牌
TOKEN=$(cat /run/secrets/sonnetdb_token)
curl -H "Authorization: Bearer $TOKEN" http://localhost:8839/api/v1/query
```

### 查看令牌状态

您可以通过管理界面或 SQL 命令查看当前所有令牌的状态：

```sql
-- 查看所有令牌（含名称、角色、创建时间、过期时间、是否有效）
SHOW TOKENS;
```

SonnetDB 的 Token 认证机制简单而强大，结合角色权限体系，为各种应用场景提供了灵活且安全的 API 访问控制方案。正确使用这套机制，可以有效防止未授权访问，保障您的时序数据安全。
