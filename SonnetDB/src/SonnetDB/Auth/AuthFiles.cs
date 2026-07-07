using System.Text.Json.Serialization;

namespace SonnetDB.Auth;

// JSON DTO：与持久化文件 1:1 对应。所有字段都用公共属性，方便 System.Text.Json 源生成器处理。

/// <summary>users.json 顶层结构。</summary>
public sealed class UserFile
{
    /// <summary>结构版本号。</summary>
    public int Version { get; set; } = 1;
    /// <summary>用户列表。</summary>
    public List<UserRecord> Users { get; set; } = new();
}

/// <summary>单条用户记录。</summary>
public sealed class UserRecord
{
    /// <summary>用户名（小写、唯一、不可为空）。</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>PBKDF2 派生密钥（Base64）。</summary>
    public string PasswordHash { get; set; } = string.Empty;
    /// <summary>PBKDF2 盐（Base64）。</summary>
    public string Salt { get; set; } = string.Empty;
    /// <summary>PBKDF2 迭代次数。</summary>
    public int Iterations { get; set; } = PasswordHasher.DefaultIterations;
    /// <summary>是否超级用户：拥有所有数据库的 Admin 权限以及用户管理权限。</summary>
    public bool IsSuperuser { get; set; }
    /// <summary>创建时间（Unix 毫秒）。</summary>
    public long CreatedAt { get; set; }
    /// <summary>已颁发的 API token 列表。</summary>
    public List<TokenRecord> Tokens { get; set; } = new();
}

/// <summary>已颁发的 API token 记录。仅持久化哈希，永不持久化明文。</summary>
public sealed class TokenRecord
{
    /// <summary>token 短 ID（用于吊销引用），Base64Url 8 字节。</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>token 的 SHA-256 哈希（64 字符小写 hex）。</summary>
    public string SecretHash { get; set; } = string.Empty;
    /// <summary>创建时间（Unix 毫秒）。</summary>
    public long CreatedAt { get; set; }
    /// <summary>最近使用时间（Unix 毫秒；可空）。</summary>
    public long? LastUsedAt { get; set; }
}

/// <summary>grants.json 顶层结构。</summary>
public sealed class GrantsFile
{
    /// <summary>结构版本号。</summary>
    public int Version { get; set; } = 1;
    /// <summary>授权列表。</summary>
    public List<GrantRecord> Grants { get; set; } = new();
}

/// <summary>(用户, 数据库, 权限级别) 三元组。同 (用户, 数据库) 对最多一条；以最高级别为准。</summary>
public sealed class GrantRecord
{
    /// <summary>用户名。</summary>
    public string User { get; set; } = string.Empty;
    /// <summary>数据库名；<c>"*"</c> 通配。</summary>
    public string Database { get; set; } = string.Empty;
    /// <summary>权限级别。</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<DatabasePermission>))]
    public DatabasePermission Permission { get; set; }
}

/// <summary>installation.json 顶层结构。</summary>
public sealed class InstallationFile
{
    /// <summary>结构版本号。</summary>
    public int Version { get; set; } = 1;

    /// <summary>当前服务器标识。</summary>
    public string ServerId { get; set; } = string.Empty;

    /// <summary>所属组织名称。</summary>
    public string Organization { get; set; } = string.Empty;

    /// <summary>初始管理员用户名（小写）。</summary>
    public string AdminUserName { get; set; } = string.Empty;

    /// <summary>初始 Bearer Token 的 token id。</summary>
    public string InitialTokenId { get; set; } = string.Empty;

    /// <summary>初始化完成时间（Unix 毫秒）。</summary>
    public long InitializedAt { get; set; }
}
