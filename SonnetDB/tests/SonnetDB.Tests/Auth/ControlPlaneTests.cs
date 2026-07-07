using SonnetDB.Auth;
using SonnetDB.Engine;
using SonnetDB.Hosting;
using SonnetDB.Sql;
using SonnetDB.Sql.Ast;
using SonnetDB.Sql.Execution;
using Xunit;

namespace SonnetDB.Tests.Auth;

/// <summary>
/// PR #34a-4：服务端 <see cref="ControlPlane"/> + <see cref="SqlExecutor"/> 控制面 DDL 集成测试。
/// </summary>
public sealed class ControlPlaneTests : IDisposable
{
    private readonly string _dir;
    private readonly UserStore _users;
    private readonly GrantsStore _grants;
    private readonly TsdbRegistry _registry;
    private readonly ControlPlane _controlPlane;

    public ControlPlaneTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "sndb-cp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        var systemDir = Path.Combine(_dir, ".system");
        Directory.CreateDirectory(systemDir);
        _users = new UserStore(systemDir);
        _grants = new GrantsStore(systemDir);
        _registry = new TsdbRegistry(_dir);
        _controlPlane = new ControlPlane(_users, _grants, _registry);
    }

    public void Dispose()
    {
        _registry.Dispose();
        TryDeleteDirectory(_dir);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"清理临时目录失败（IO）：{path} / {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"清理临时目录失败（权限）：{path} / {ex.Message}");
        }
    }

    [Fact]
    public void CreateUser_ViaSql_PersistsAndAuthenticates()
    {
        var bootstrap = Tsdb.Open(new SonnetDB.Engine.TsdbOptions { RootDirectory = Path.Combine(_dir, "_bootstrap") });
        try
        {
            SqlExecutor.Execute(bootstrap, "CREATE USER alice WITH PASSWORD 'pa$$'", _controlPlane);
            Assert.True(_users.VerifyPassword("alice", "pa$$"));
        }
        finally { bootstrap.Dispose(); }
    }

    [Fact]
    public void CreateDatabase_ViaSql_RegistersInRegistry()
    {
        using var bootstrap = Tsdb.Open(new SonnetDB.Engine.TsdbOptions { RootDirectory = Path.Combine(_dir, "_bootstrap") });
        SqlExecutor.Execute(bootstrap, "CREATE DATABASE metrics", _controlPlane);
        Assert.True(_registry.TryGet("metrics", out _));
    }

    [Fact]
    public void GrantAndRevoke_ViaSql_FlowsToGrantsStore()
    {
        using var bootstrap = Tsdb.Open(new SonnetDB.Engine.TsdbOptions { RootDirectory = Path.Combine(_dir, "_bootstrap") });
        SqlExecutor.Execute(bootstrap, "CREATE USER alice WITH PASSWORD 'p'", _controlPlane);
        SqlExecutor.Execute(bootstrap, "CREATE DATABASE metrics", _controlPlane);

        SqlExecutor.Execute(bootstrap, "GRANT WRITE ON DATABASE metrics TO alice", _controlPlane);
        Assert.Equal(DatabasePermission.Write, _grants.GetPermission("alice", "metrics"));

        SqlExecutor.Execute(bootstrap, "REVOKE ON DATABASE metrics FROM alice", _controlPlane);
        Assert.Equal(DatabasePermission.None, _grants.GetPermission("alice", "metrics"));
    }

    [Fact]
    public void DropUser_ViaSql_AlsoDeletesGrants()
    {
        using var bootstrap = Tsdb.Open(new SonnetDB.Engine.TsdbOptions { RootDirectory = Path.Combine(_dir, "_bootstrap") });
        SqlExecutor.Execute(bootstrap, "CREATE USER alice WITH PASSWORD 'p'", _controlPlane);
        SqlExecutor.Execute(bootstrap, "CREATE DATABASE metrics", _controlPlane);
        SqlExecutor.Execute(bootstrap, "GRANT READ ON DATABASE metrics TO alice", _controlPlane);

        SqlExecutor.Execute(bootstrap, "DROP USER alice", _controlPlane);
        Assert.False(_users.Exists("alice"));
        Assert.Equal(DatabasePermission.None, _grants.GetPermission("alice", "metrics"));
    }

    [Fact]
    public void DropDatabase_ViaSql_RemovesFromRegistry_AndCascadesGrants()
    {
        using var bootstrap = Tsdb.Open(new SonnetDB.Engine.TsdbOptions { RootDirectory = Path.Combine(_dir, "_bootstrap") });
        SqlExecutor.Execute(bootstrap, "CREATE USER alice WITH PASSWORD 'p'", _controlPlane);
        SqlExecutor.Execute(bootstrap, "CREATE DATABASE metrics", _controlPlane);
        SqlExecutor.Execute(bootstrap, "GRANT READ ON DATABASE metrics TO alice", _controlPlane);

        SqlExecutor.Execute(bootstrap, "DROP DATABASE metrics", _controlPlane);
        Assert.False(_registry.TryGet("metrics", out _));
        Assert.Equal(DatabasePermission.None, _grants.GetPermission("alice", "metrics"));
    }

    [Fact]
    public void AlterUser_ViaSql_ChangesPasswordAndRevokesTokens()
    {
        using var bootstrap = Tsdb.Open(new SonnetDB.Engine.TsdbOptions { RootDirectory = Path.Combine(_dir, "_bootstrap") });
        SqlExecutor.Execute(bootstrap, "CREATE USER alice WITH PASSWORD 'old'", _controlPlane);
        var (token, _) = _users.IssueToken("alice");

        SqlExecutor.Execute(bootstrap, "ALTER USER alice WITH PASSWORD 'new'", _controlPlane);
        Assert.True(_users.VerifyPassword("alice", "new"));
        Assert.False(_users.VerifyPassword("alice", "old"));
        Assert.False(_users.TryAuthenticate(token, out _));
    }

    [Fact]
    public void Grant_OnNonexistentDatabase_Throws()
    {
        using var bootstrap = Tsdb.Open(new SonnetDB.Engine.TsdbOptions { RootDirectory = Path.Combine(_dir, "_bootstrap") });
        SqlExecutor.Execute(bootstrap, "CREATE USER alice WITH PASSWORD 'p'", _controlPlane);
        Assert.Throws<InvalidOperationException>(() =>
            SqlExecutor.Execute(bootstrap, "GRANT READ ON DATABASE missing TO alice", _controlPlane));
    }

    [Fact]
    public void ListUsers_ReturnsCreatedUsersOrderedByName()
    {
        using var bootstrap = Tsdb.Open(new SonnetDB.Engine.TsdbOptions { RootDirectory = Path.Combine(_dir, "_bootstrap") });
        SqlExecutor.Execute(bootstrap, "CREATE USER bob WITH PASSWORD 'p'", _controlPlane);
        SqlExecutor.Execute(bootstrap, "CREATE USER alice WITH PASSWORD 'p'", _controlPlane);

        var list = _controlPlane.ListUsers();
        Assert.Equal(2, list.Count);
        Assert.Equal("alice", list[0].Name);
        Assert.Equal("bob", list[1].Name);
        Assert.False(list[0].IsSuperuser);
        Assert.Equal(0, list[0].TokenCount);
    }

    [Fact]
    public void ListGrants_NullFilter_ReturnsAll()
    {
        using var bootstrap = Tsdb.Open(new SonnetDB.Engine.TsdbOptions { RootDirectory = Path.Combine(_dir, "_bootstrap") });
        SqlExecutor.Execute(bootstrap, "CREATE DATABASE m1", _controlPlane);
        SqlExecutor.Execute(bootstrap, "CREATE DATABASE m2", _controlPlane);
        SqlExecutor.Execute(bootstrap, "CREATE USER alice WITH PASSWORD 'p'", _controlPlane);
        SqlExecutor.Execute(bootstrap, "CREATE USER bob WITH PASSWORD 'p'", _controlPlane);
        SqlExecutor.Execute(bootstrap, "GRANT READ ON DATABASE m1 TO alice", _controlPlane);
        SqlExecutor.Execute(bootstrap, "GRANT WRITE ON DATABASE m2 TO bob", _controlPlane);

        var all = _controlPlane.ListGrants(null);
        Assert.Equal(2, all.Count);

        var aliceOnly = _controlPlane.ListGrants("alice");
        Assert.Single(aliceOnly);
        Assert.Equal("m1", aliceOnly[0].Database);
        Assert.Equal(GrantPermission.Read, aliceOnly[0].Permission);
    }

    [Fact]
    public void ListDatabases_ReflectsRegistry()
    {
        using var bootstrap = Tsdb.Open(new SonnetDB.Engine.TsdbOptions { RootDirectory = Path.Combine(_dir, "_bootstrap") });
        SqlExecutor.Execute(bootstrap, "CREATE DATABASE alpha", _controlPlane);
        SqlExecutor.Execute(bootstrap, "CREATE DATABASE beta", _controlPlane);
        var dbs = _controlPlane.ListDatabases();
        Assert.Contains("alpha", dbs);
        Assert.Contains("beta", dbs);
    }

    [Fact]
    public void TokenStatements_ViaSql_IssueListAndRevoke()
    {
        using var bootstrap = Tsdb.Open(new SonnetDB.Engine.TsdbOptions { RootDirectory = Path.Combine(_dir, "_bootstrap") });
        SqlExecutor.Execute(bootstrap, "CREATE USER alice WITH PASSWORD 'p'", _controlPlane);

        var issued = Assert.IsType<SelectExecutionResult>(
            SqlExecutor.ExecuteControlPlaneStatement(SqlParser.Parse("ISSUE TOKEN FOR alice"), _controlPlane));
        Assert.Single(issued.Rows);
        var tokenId = Assert.IsType<string>(issued.Rows[0][0]);
        var token = Assert.IsType<string>(issued.Rows[0][1]);
        Assert.True(_users.TryAuthenticate(token, out var user));
        Assert.Equal("alice", user.UserName);

        var listed = Assert.IsType<SelectExecutionResult>(
            SqlExecutor.ExecuteControlPlaneStatement(SqlParser.Parse("SHOW TOKENS FOR alice"), _controlPlane));
        Assert.Single(listed.Rows);
        Assert.Equal(tokenId, listed.Rows[0][0]);

        SqlExecutor.ExecuteControlPlaneStatement(SqlParser.Parse($"REVOKE TOKEN '{tokenId}'"), _controlPlane);
        Assert.False(_users.TryAuthenticate(token, out _));
    }

    [Fact]
    public void TokenStatements_ViaSql_WithQuotedHyphenatedUser_Work()
    {
        _controlPlane.CreateUser("ops-admin", "p", isSuperuser: false);

        var issued = Assert.IsType<SelectExecutionResult>(
            SqlExecutor.ExecuteControlPlaneStatement(SqlParser.Parse("ISSUE TOKEN FOR 'ops-admin'"), _controlPlane));
        Assert.Single(issued.Rows);
        var tokenId = Assert.IsType<string>(issued.Rows[0][0]);
        var token = Assert.IsType<string>(issued.Rows[0][1]);
        Assert.True(_users.TryAuthenticate(token, out var user));
        Assert.Equal("ops-admin", user.UserName);

        var listed = Assert.IsType<SelectExecutionResult>(
            SqlExecutor.ExecuteControlPlaneStatement(SqlParser.Parse("SHOW TOKENS FOR 'ops-admin'"), _controlPlane));
        Assert.Single(listed.Rows);
        Assert.Equal(tokenId, listed.Rows[0][0]);

        SqlExecutor.ExecuteControlPlaneStatement(SqlParser.Parse($"REVOKE TOKEN '{tokenId}'"), _controlPlane);
        Assert.False(_users.TryAuthenticate(token, out _));
    }

    [Fact]
    public void Grant_OnWildcardDatabase_DoesNotRequireExistingDatabase()
    {
        using var bootstrap = Tsdb.Open(new SonnetDB.Engine.TsdbOptions { RootDirectory = Path.Combine(_dir, "_bootstrap") });
        SqlExecutor.Execute(bootstrap, "CREATE USER alice WITH PASSWORD 'p'", _controlPlane);
        SqlExecutor.Execute(bootstrap, "GRANT ADMIN ON DATABASE * TO alice", _controlPlane);
        Assert.Equal(DatabasePermission.Admin, _grants.GetPermission("alice", "any-db"));
    }

    [Fact]
    public void ControlPlaneDdl_WithoutControlPlane_Throws()
    {
        using var bootstrap = Tsdb.Open(new SonnetDB.Engine.TsdbOptions { RootDirectory = Path.Combine(_dir, "_bootstrap") });
        Assert.Throws<NotSupportedException>(() =>
            SqlExecutor.Execute(bootstrap, "CREATE USER alice WITH PASSWORD 'p'"));
    }
}
