using SonnetDB.Engine;
using SonnetDB.Sql;
using SonnetDB.Sql.Execution;
using Xunit;

namespace SonnetDB.Core.Tests.Sql;

/// <summary>
/// PR #54 — PID 内置函数 SQL 端到端集成测试。
/// 覆盖 <c>pid_series</c> 行级窗口形态、<c>pid</c> 聚合形态、参数校验，以及
/// <c>INSERT … SELECT pid_series(...)</c> 控制回写场景。
/// </summary>
public sealed class SqlExecutorPidFunctionTests : IDisposable
{
    private readonly string _root;

    public SqlExecutorPidFunctionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sndb-pid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    private TsdbOptions Options() => new() { RootDirectory = _root };

    private static Tsdb OpenWithSchema(TsdbOptions options)
    {
        var db = Tsdb.Open(options);
        SqlExecutor.Execute(db,
            "CREATE MEASUREMENT reactor (device TAG, temperature FIELD FLOAT)");
        SqlExecutor.Execute(db,
            "CREATE MEASUREMENT actuator (device TAG, valve FIELD FLOAT)");
        return db;
    }

    private static SelectExecutionResult Select(Tsdb db, string sql)
        => Assert.IsType<SelectExecutionResult>(SqlExecutor.Execute(db, sql));

    // ── pid_series 行级 ─────────────────────────────────────────────────

    [Fact]
    public void Select_PidSeries_ProducesPerRowControlSignal()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO reactor (time, device, temperature) VALUES " +
            "(0, 'r1', 0), (1000, 'r1', 4), (2000, 'r1', 7)");

        var r = Select(db,
            "SELECT pid_series(temperature, 10, 1, 1, 1) FROM reactor");

        Assert.Equal(3, r.Rows.Count);
        // 与 PidControllerTests.PidSeriesEvaluator_OutputsControlSignalPerRow 同步：
        Assert.Equal(10.0, (double)r.Rows[0][0]!, precision: 9);
        Assert.Equal(8.0, (double)r.Rows[1][0]!, precision: 9);
        Assert.Equal(9.0, (double)r.Rows[2][0]!, precision: 9);
    }

    [Fact]
    public void Select_PidSeries_MixesWithTimeAndField()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO reactor (time, device, temperature) VALUES " +
            "(0, 'r1', 0), (1000, 'r1', 4)");

        var r = Select(db,
            "SELECT time, temperature, pid_series(temperature, 10, 1, 0, 0) AS u FROM reactor");
        Assert.Equal(["time", "temperature", "u"], r.Columns);
        // ki=0, kd=0 → u = Kp*e
        Assert.Equal(10.0, (double)r.Rows[0][2]!, precision: 9);
        Assert.Equal(6.0, (double)r.Rows[1][2]!, precision: 9);
    }

    // ── pid 聚合形态 ────────────────────────────────────────────────────

    [Fact]
    public void Select_PidAggregate_ReturnsLastControlSignal()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO reactor (time, device, temperature) VALUES " +
            "(0, 'r1', 0), (1000, 'r1', 4), (2000, 'r1', 7)");

        var r = Select(db, "SELECT pid(temperature, 10, 1, 1, 1) FROM reactor");
        Assert.Single(r.Rows);
        Assert.Equal(9.0, (double)r.Rows[0][0]!, precision: 9);
    }

    [Fact]
    public void Select_PidAggregate_GroupedByTimeBucket()
    {
        using var db = OpenWithSchema(Options());
        // 桶 1（[0,1000)）：行 ts=0，pv=0 → u=10
        // 桶 2（[1000,2000)）：行 ts=1000 pv=4，新桶 → 桶内首行只算 P → u=Kp*(10-4)=6
        SqlExecutor.Execute(db,
            "INSERT INTO reactor (time, device, temperature) VALUES " +
            "(0, 'r1', 0), (1000, 'r1', 4)");

        var r = Select(db,
            "SELECT pid(temperature, 10, 1, 1, 1) FROM reactor GROUP BY time(1s)");

        Assert.Equal(2, r.Rows.Count);
        Assert.Equal(10.0, (double)r.Rows[0][0]!, precision: 9);
        Assert.Equal(6.0, (double)r.Rows[1][0]!, precision: 9);
    }

    // ── pid_series 结果可被重新 INSERT 回写作为控制量（模拟控制回路） ───────────

    [Fact]
    public void PidSeries_OutputsCanBePersistedAsActuatorCommands()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO reactor (time, device, temperature) VALUES " +
            "(0, 'r1', 0), (1000, 'r1', 4), (2000, 'r1', 7)");

        // 在嵌入式场景下，应用代码先查询 pid_series 结果、再 INSERT 到 actuator。
        var r = Select(db,
            "SELECT time, pid_series(temperature, 10, 1, 1, 1) FROM reactor");
        Assert.Equal(3, r.Rows.Count);

        var values = new System.Text.StringBuilder();
        for (int i = 0; i < r.Rows.Count; i++)
        {
            if (i > 0) values.Append(", ");
            values.Append('(').Append(r.Rows[i][0]).Append(", 'r1', ")
                  .Append(((double)r.Rows[i][1]!).ToString(System.Globalization.CultureInfo.InvariantCulture))
                  .Append(')');
        }
        SqlExecutor.Execute(db, $"INSERT INTO actuator (time, device, valve) VALUES {values}");

        var actuator = Select(db, "SELECT time, valve FROM actuator");
        Assert.Equal(3, actuator.Rows.Count);
        Assert.Equal(10.0, (double)actuator.Rows[0][1]!, precision: 9);
        Assert.Equal(8.0, (double)actuator.Rows[1][1]!, precision: 9);
        Assert.Equal(9.0, (double)actuator.Rows[2][1]!, precision: 9);
    }

    // ── 参数校验 ────────────────────────────────────────────────────────

    [Fact]
    public void Select_PidSeries_RejectsWrongArgumentCount()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO reactor (time, device, temperature) VALUES (0, 'r1', 0)");

        Assert.Throws<InvalidOperationException>(() =>
            Select(db, "SELECT pid_series(temperature, 10, 1, 1) FROM reactor"));
    }

    [Fact]
    public void Select_Pid_RejectsWrongArgumentCount()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO reactor (time, device, temperature) VALUES (0, 'r1', 0)");

        Assert.Throws<InvalidOperationException>(() =>
            Select(db, "SELECT pid(temperature, 10, 1) FROM reactor"));
    }

    [Fact]
    public void Select_Pid_RejectsTagColumn()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO reactor (time, device, temperature) VALUES (0, 'r1', 0)");

        Assert.Throws<InvalidOperationException>(() =>
            Select(db, "SELECT pid(device, 10, 1, 1, 1) FROM reactor"));
    }

    [Fact]
    public void Select_PidSeries_AcceptsNegativeGains()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO reactor (time, device, temperature) VALUES (0, 'r1', 0), (1000, 'r1', 5)");

        // kp=-1：第一行 u = -1*10 = -10；第二行 e=5 u = -1*5 = -5（ki=0, kd=0）
        var r = Select(db, "SELECT pid_series(temperature, 10, -1, 0, 0) FROM reactor");
        Assert.Equal(-10.0, (double)r.Rows[0][0]!, precision: 9);
        Assert.Equal(-5.0, (double)r.Rows[1][0]!, precision: 9);
    }

    // ── pid_estimate 阶跃响应自动整定 ───────────────────────────────────

    /// <summary>
    /// 生成 FOPDT 阶跃响应数据：y(t) = y0 + K·Δu·(1 − exp(−(t − θ)/τ))。
    /// </summary>
    private static (long Ts, double V)[] FopdtStep(
        double K, double tau, double theta, int n, long dtMs)
    {
        var samples = new (long, double)[n];
        for (int i = 0; i < n; i++)
        {
            long t = i * dtMs;
            double e = t - theta;
            double y = e <= 0.0 ? 0.0 : K * (1.0 - Math.Exp(-e / tau));
            samples[i] = (t, y);
        }
        return samples;
    }

    private static void InsertFopdtStep(Tsdb db, double K, double tau, double theta, int n, long dtMs)
    {
        var sb = new System.Text.StringBuilder("INSERT INTO reactor (time, device, temperature) VALUES ");
        var rows = FopdtStep(K, tau, theta, n, dtMs);
        for (int i = 0; i < rows.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append('(').Append(rows[i].Ts).Append(", 'r1', ")
              .Append(rows[i].V.ToString(System.Globalization.CultureInfo.InvariantCulture))
              .Append(')');
        }
        SqlExecutor.Execute(db, sb.ToString());
    }

    [Fact]
    public void Select_PidEstimate_ZN_ReturnsTuningJson()
    {
        using var db = OpenWithSchema(Options());
        // K=2, τ=100ms, θ=200ms ⇒ ZN 理论值 Kp≈0.3, Ki≈7.5e-4/ms, Kd≈30ms
        InsertFopdtStep(db, K: 2.0, tau: 100.0, theta: 200.0, n: 300, dtMs: 5);

        var r = Select(db,
            "SELECT pid_estimate(temperature, 'zn', 1.0, 0.1, 0.1, NULL) FROM reactor");

        Assert.Single(r.Rows);
        var json = (string)r.Rows[0][0]!;
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        double kp = doc.RootElement.GetProperty("kp").GetDouble();
        double ki = doc.RootElement.GetProperty("ki").GetDouble();
        double kd = doc.RootElement.GetProperty("kd").GetDouble();

        Assert.InRange(kp, 0.27, 0.33);
        Assert.InRange(ki, 0.00060, 0.00090);
        Assert.InRange(kd, 25.0, 35.0);
    }

    [Fact]
    public void Select_PidEstimate_Imc_AcceptsExplicitLambda()
    {
        using var db = OpenWithSchema(Options());
        InsertFopdtStep(db, K: 2.0, tau: 100.0, theta: 20.0, n: 400, dtMs: 1);

        var r = Select(db,
            "SELECT pid_estimate(temperature, 'imc', 1.0, 0.1, 0.1, 50) FROM reactor");

        Assert.Single(r.Rows);
        var json = (string)r.Rows[0][0]!;
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        double kp = doc.RootElement.GetProperty("kp").GetDouble();
        Assert.True(kp > 0, $"IMC Kp 应为正，实际 {kp}");
    }

    [Fact]
    public void Select_PidEstimate_NullArguments_UseDefaults()
    {
        using var db = OpenWithSchema(Options());
        InsertFopdtStep(db, K: 2.0, tau: 100.0, theta: 200.0, n: 300, dtMs: 5);

        // 全部可选参数传 NULL → method=ZN, Δu=1, IF=0.1, FF=0.1, λ=θ
        var r = Select(db,
            "SELECT pid_estimate(temperature, NULL, NULL, NULL, NULL, NULL) FROM reactor");

        Assert.Single(r.Rows);
        Assert.Contains("\"kp\":", (string)r.Rows[0][0]!);
    }

    [Fact]
    public void Select_PidEstimate_RejectsUnknownMethod()
    {
        using var db = OpenWithSchema(Options());
        InsertFopdtStep(db, K: 2.0, tau: 100.0, theta: 200.0, n: 300, dtMs: 5);

        Assert.Throws<InvalidOperationException>(() => Select(db,
            "SELECT pid_estimate(temperature, 'foobar', 1.0, 0.1, 0.1, NULL) FROM reactor"));
    }

    [Fact]
    public void Select_PidEstimate_RejectsWrongArgumentCount()
    {
        using var db = OpenWithSchema(Options());
        InsertFopdtStep(db, K: 2.0, tau: 100.0, theta: 200.0, n: 300, dtMs: 5);

        Assert.Throws<InvalidOperationException>(() => Select(db,
            "SELECT pid_estimate(temperature, 'zn', 1.0) FROM reactor"));
    }

    [Fact]
    public void Select_PidEstimate_EmptyResult_ReturnsNoRows()
    {
        using var db = OpenWithSchema(Options());
        // 没有插入任何数据
        var r = Select(db,
            "SELECT pid_estimate(temperature, 'zn', 1.0, 0.1, 0.1, NULL) FROM reactor");

        Assert.Empty(r.Rows);
    }
}
