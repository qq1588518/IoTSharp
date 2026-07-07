using SonnetDB.Engine;
using SonnetDB.Engine.Compaction;
using SonnetDB.Model;
using SonnetDB.Sql;
using SonnetDB.Sql.Execution;
using Xunit;

namespace SonnetDB.Core.Tests.Sql;

/// <summary>
/// PR #53 — Tier 3 窗口函数（difference / derivative / rate / cumulative_sum /
/// moving_average / ewma / fill / locf / interpolate / state_changes 等）
/// 的 SQL 端到端集成测试。
/// </summary>
public sealed class SqlExecutorWindowFunctionTests : IDisposable
{
    private readonly string _root;

    public SqlExecutorWindowFunctionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sndb-window-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    private TsdbOptions Options() => new() { RootDirectory = _root };

    private TsdbOptions OptionsWithoutBackgroundWorkers() => new()
    {
        RootDirectory = _root,
        BackgroundFlush = new BackgroundFlushOptions { Enabled = false },
        Compaction = new CompactionPolicy { Enabled = false },
    };

    private static Tsdb OpenWithSchema(TsdbOptions options)
    {
        var db = Tsdb.Open(options);
        SqlExecutor.Execute(db,
            "CREATE MEASUREMENT cpu (host TAG, usage FIELD FLOAT, label FIELD STRING)");
        return db;
    }

    private static SelectExecutionResult Select(Tsdb db, string sql)
        => Assert.IsType<SelectExecutionResult>(SqlExecutor.Execute(db, sql));

    // ── difference / derivative / rate ──────────────────────────────────

    [Fact]
    public void Select_Difference_OutputsRowDeltas()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO cpu (time, host, usage) VALUES " +
            "(0, 'h1', 10), (1000, 'h1', 13), (2000, 'h1', 11)");

        var r = Select(db, "SELECT time, difference(usage) FROM cpu");
        Assert.Equal(3, r.Rows.Count);
        Assert.Null(r.Rows[0][1]);
        Assert.Equal(3.0, (double)r.Rows[1][1]!);
        Assert.Equal(-2.0, (double)r.Rows[2][1]!);
    }

    [Fact]
    public void Select_Derivative_PerSecond()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO cpu (time, host, usage) VALUES " +
            "(0, 'h1', 0), (1000, 'h1', 10), (3000, 'h1', 40)");

        var r = Select(db, "SELECT derivative(usage, 1s) FROM cpu");
        Assert.Null(r.Rows[0][0]);
        Assert.Equal(10.0, (double)r.Rows[1][0]!);
        Assert.Equal(15.0, (double)r.Rows[2][0]!);
    }

    [Fact]
    public void Select_NonNegativeDerivative_DropsResetPoints()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO cpu (time, host, usage) VALUES " +
            "(0, 'h1', 100), (1000, 'h1', 50), (2000, 'h1', 80)");

        var r = Select(db, "SELECT non_negative_derivative(usage) FROM cpu");
        Assert.Null(r.Rows[0][0]);
        Assert.Null(r.Rows[1][0]);
        Assert.Equal(30.0, (double)r.Rows[2][0]!);
    }

    [Fact]
    public void Select_Rate_DefaultsToPerSecond()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO cpu (time, host, usage) VALUES (0, 'h1', 0), (500, 'h1', 5)");

        var r = Select(db, "SELECT rate(usage) FROM cpu");
        Assert.Null(r.Rows[0][0]);
        // (5-0) * 1000 / 500 = 10 / s
        Assert.Equal(10.0, (double)r.Rows[1][0]!);
    }

    // ── cumulative_sum / integral ────────────────────────────────────────

    [Fact]
    public void Select_CumulativeSum_RunningTotal()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO cpu (time, host, usage) VALUES " +
            "(0, 'h1', 1), (1, 'h1', 2), (2, 'h1', 3), (3, 'h1', 4)");

        var r = Select(db, "SELECT cumulative_sum(usage) FROM cpu");
        Assert.Equal(1.0, (double)r.Rows[0][0]!);
        Assert.Equal(3.0, (double)r.Rows[1][0]!);
        Assert.Equal(6.0, (double)r.Rows[2][0]!);
        Assert.Equal(10.0, (double)r.Rows[3][0]!);
    }

    [Fact]
    public void Select_RunningSumAlias_ReturnsSameOutputAsCumulativeSum()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO cpu (time, host, usage) VALUES " +
            "(0, 'h1', 1), (1, 'h1', 2), (2, 'h1', 3)");

        var cumulative = Select(db, "SELECT cumulative_sum(usage) FROM cpu");
        var running = Select(db, "SELECT running_sum(usage) FROM cpu");

        Assert.Equal(cumulative.Rows.Select(static r => r[0]).ToArray(), running.Rows.Select(static r => r[0]).ToArray());
    }

    [Fact]
    public void Select_RunningMinMax_ReturnCumulativeExtremes()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO cpu (time, host, usage) VALUES " +
            "(0, 'h1', 5), (1, 'h1', 3), (2, 'h1', 7)");

        var r = Select(db, "SELECT running_min(usage), running_max(usage) FROM cpu");

        Assert.Equal(5.0, (double)r.Rows[0][0]!);
        Assert.Equal(5.0, (double)r.Rows[0][1]!);
        Assert.Equal(3.0, (double)r.Rows[1][0]!);
        Assert.Equal(5.0, (double)r.Rows[1][1]!);
        Assert.Equal(3.0, (double)r.Rows[2][0]!);
        Assert.Equal(7.0, (double)r.Rows[2][1]!);
    }

    [Fact]
    public void Select_Integral_OnConstant_EqualsValueTimesSeconds()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO cpu (time, host, usage) VALUES " +
            "(0, 'h1', 10), (1000, 'h1', 10), (2000, 'h1', 10)");

        var r = Select(db, "SELECT integral(usage, 1s) FROM cpu");
        Assert.Equal(0.0, (double)r.Rows[0][0]!);
        Assert.Equal(10.0, (double)r.Rows[1][0]!, precision: 6);
        Assert.Equal(20.0, (double)r.Rows[2][0]!, precision: 6);
    }

    // ── moving_average / ewma ────────────────────────────────────────────

    [Fact]
    public void Select_MovingAverage_NPointWindow()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO cpu (time, host, usage) VALUES " +
            "(0, 'h1', 1), (1, 'h1', 2), (2, 'h1', 3), (3, 'h1', 4), (4, 'h1', 5)");

        var r = Select(db, "SELECT moving_average(usage, 3) FROM cpu");
        Assert.Null(r.Rows[0][0]);
        Assert.Null(r.Rows[1][0]);
        Assert.Equal(2.0, (double)r.Rows[2][0]!);
        Assert.Equal(3.0, (double)r.Rows[3][0]!);
        Assert.Equal(4.0, (double)r.Rows[4][0]!);
    }

    [Fact]
    public void Select_Ewma_Alpha05()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO cpu (time, host, usage) VALUES " +
            "(0, 'h1', 10), (1, 'h1', 20), (2, 'h1', 30)");

        var r = Select(db, "SELECT ewma(usage, 0.5) FROM cpu");
        Assert.Equal(10.0, (double)r.Rows[0][0]!);
        Assert.Equal(15.0, (double)r.Rows[1][0]!, precision: 6);
        Assert.Equal(22.5, (double)r.Rows[2][0]!, precision: 6);
    }

    [Fact]
    public void Select_MovingAverage_LargeDataset_StreamingPathKeepsAllocationBudget()
    {
        const int PointCount = 30_000;
        using var db = OpenWithSchema(OptionsWithoutBackgroundWorkers());
        var tags = new Dictionary<string, string> { ["host"] = "h1" };
        var points = new Point[PointCount];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = Point.Create(
                "cpu",
                i,
                tags,
                new Dictionary<string, FieldValue> { ["usage"] = FieldValue.FromDouble(i) });
        }

        Assert.Equal(PointCount, db.WriteMany(points));

        var warmup = Select(db, "SELECT moving_average(usage, 60) FROM cpu");
        Assert.Equal(PointCount, warmup.Rows.Count);

        long allocated = MeasureAllocatedBytes(() =>
        {
            var result = Select(db, "SELECT moving_average(usage, 60) FROM cpu");
            Assert.Equal(PointCount, result.Rows.Count);
            Assert.Null(result.Rows[0][0]);
            Assert.Equal(29.5, (double)result.Rows[59][0]!);
        });

        const long BudgetBytes = 32L * 1024 * 1024;
        Assert.True(
            allocated < BudgetBytes,
            $"Streaming window query allocated too much memory: {allocated} bytes.");
    }

    // ── fill / locf / interpolate ────────────────────────────────────────

    [Fact]
    public void Select_Fill_ReplacesNullsAcrossFieldsOuterJoin()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "CREATE MEASUREMENT m (host TAG, a FIELD FLOAT, b FIELD FLOAT)");
        // ts 0 仅有 a；ts 1000 仅有 b。outer-join 后 a 在 ts=1000 缺失。
        SqlExecutor.Execute(db,
            "INSERT INTO m (time, host, a) VALUES (0, 'h1', 5)");
        SqlExecutor.Execute(db,
            "INSERT INTO m (time, host, b) VALUES (1000, 'h1', 7)");

        var r = Select(db, "SELECT time, b, fill(a, -1) FROM m");
        Assert.Equal(2, r.Rows.Count);
        Assert.Equal(5.0, (double)r.Rows[0][2]!);
        Assert.Equal(-1.0, (double)r.Rows[1][2]!);
    }

    [Fact]
    public void Select_Locf_CarriesForwardLastValue()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "CREATE MEASUREMENT m (host TAG, a FIELD FLOAT, b FIELD FLOAT)");
        SqlExecutor.Execute(db,
            "INSERT INTO m (time, host, a) VALUES (0, 'h1', 10)");
        // ts 1000 / 2000 仅有 b，使 a 在这两行外连接为 null
        SqlExecutor.Execute(db,
            "INSERT INTO m (time, host, b) VALUES (1000, 'h1', 1), (2000, 'h1', 2)");
        SqlExecutor.Execute(db,
            "INSERT INTO m (time, host, a) VALUES (3000, 'h1', 99)");

        var r = Select(db, "SELECT b, locf(a) FROM m");
        Assert.Equal(4, r.Rows.Count);
        Assert.Equal(10.0, (double)r.Rows[0][1]!);
        Assert.Equal(10.0, (double)r.Rows[1][1]!);
        Assert.Equal(10.0, (double)r.Rows[2][1]!);
        Assert.Equal(99.0, (double)r.Rows[3][1]!);
    }

    [Fact]
    public void Select_Interpolate_LinearAcrossOuterJoinGaps()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "CREATE MEASUREMENT m (host TAG, a FIELD FLOAT, b FIELD FLOAT)");
        SqlExecutor.Execute(db,
            "INSERT INTO m (time, host, a) VALUES (0, 'h1', 0), (40, 'h1', 40)");
        SqlExecutor.Execute(db,
            "INSERT INTO m (time, host, b) VALUES (10, 'h1', 1), (20, 'h1', 2), (30, 'h1', 3)");

        var r = Select(db, "SELECT time, b, interpolate(a) FROM m");
        Assert.Equal(5, r.Rows.Count);
        Assert.Equal(0.0, (double)r.Rows[0][2]!);
        Assert.Equal(10.0, (double)r.Rows[1][2]!, precision: 6);
        Assert.Equal(20.0, (double)r.Rows[2][2]!, precision: 6);
        Assert.Equal(30.0, (double)r.Rows[3][2]!, precision: 6);
        Assert.Equal(40.0, (double)r.Rows[4][2]!);
    }

    // ── 状态分析 ────────────────────────────────────────────────────────

    [Fact]
    public void Select_StateChanges_CountsTransitions()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO cpu (time, host, label) VALUES " +
            "(0, 'h1', 'A'), (1, 'h1', 'A'), (2, 'h1', 'B'), (3, 'h1', 'A')");

        var r = Select(db, "SELECT state_changes(label) FROM cpu");
        Assert.Equal(0L, r.Rows[0][0]);
        Assert.Equal(0L, r.Rows[1][0]);
        Assert.Equal(1L, r.Rows[2][0]);
        Assert.Equal(2L, r.Rows[3][0]);
    }

    [Fact]
    public void Select_StateDuration_ResetsOnChange()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO cpu (time, host, label) VALUES " +
            "(0, 'h1', 'A'), (100, 'h1', 'A'), (300, 'h1', 'B'), (500, 'h1', 'B')");

        var r = Select(db, "SELECT state_duration(label) FROM cpu");
        Assert.Equal(0L, r.Rows[0][0]);
        Assert.Equal(100L, r.Rows[1][0]);
        Assert.Equal(0L, r.Rows[2][0]);
        Assert.Equal(200L, r.Rows[3][0]);
    }

    // ── 与其它投影组合 ──────────────────────────────────────────────────

    [Fact]
    public void Select_WindowMixedWithTimeAndField()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO cpu (time, host, usage) VALUES " +
            "(0, 'h1', 10), (1, 'h1', 13)");

        var r = Select(db, "SELECT time, host, usage, difference(usage) AS diff FROM cpu");
        Assert.Equal(["time", "host", "usage", "diff"], r.Columns);
        Assert.Equal(0L, r.Rows[0][0]);
        Assert.Equal("h1", r.Rows[0][1]);
        Assert.Equal(10.0, (double)r.Rows[0][2]!);
        Assert.Null(r.Rows[0][3]);
        Assert.Equal(3.0, (double)r.Rows[1][3]!);
    }

    [Fact]
    public void Select_WindowAndAggregate_CannotBeMixed()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO cpu (time, host, usage) VALUES (0, 'h1', 1)");

        Assert.Throws<InvalidOperationException>(() =>
            Select(db, "SELECT difference(usage), avg(usage) FROM cpu"));
    }

    // ── 错误处理 ────────────────────────────────────────────────────────

    [Fact]
    public void Select_MovingAverage_RejectsZeroN()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO cpu (time, host, usage) VALUES (0, 'h1', 1)");

        Assert.Throws<InvalidOperationException>(() =>
            Select(db, "SELECT moving_average(usage, 0) FROM cpu"));
    }

    [Fact]
    public void Select_Ewma_RejectsAlphaOutOfRange()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO cpu (time, host, usage) VALUES (0, 'h1', 1)");

        Assert.Throws<InvalidOperationException>(() =>
            Select(db, "SELECT ewma(usage, 1.5) FROM cpu"));
    }

    [Fact]
    public void Select_Window_RejectsTagColumn()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO cpu (time, host, usage) VALUES (0, 'h1', 1)");

        Assert.Throws<InvalidOperationException>(() =>
            Select(db, "SELECT difference(host) FROM cpu"));
    }

    [Fact]
    public void Select_Difference_RejectsStringField()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO cpu (time, host, label) VALUES (0, 'h1', 'A')");

        Assert.Throws<InvalidOperationException>(() =>
            Select(db, "SELECT difference(label) FROM cpu"));
    }

    private static long MeasureAllocatedBytes(Action action)
    {
        action();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
