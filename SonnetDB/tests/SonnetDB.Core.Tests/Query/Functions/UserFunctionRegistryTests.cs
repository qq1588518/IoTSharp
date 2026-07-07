using SonnetDB.Catalog;
using SonnetDB.Engine;
using SonnetDB.Query;
using SonnetDB.Query.Functions;
using SonnetDB.Sql;
using SonnetDB.Sql.Ast;
using SonnetDB.Sql.Execution;
using SonnetDB.Storage.Format;
using Xunit;

namespace SonnetDB.Core.Tests.Query.Functions;

/// <summary>
/// PR #56 — 用户自定义函数（UDF）注册 API 端到端测试。
/// </summary>
public sealed class UserFunctionRegistryTests : IDisposable
{
    private readonly string _root;
    private readonly Tsdb _db;

    public UserFunctionRegistryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sndb-udf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _db = Tsdb.Open(new TsdbOptions { RootDirectory = _root });
        SqlExecutor.Execute(_db, "CREATE MEASUREMENT meter (device TAG, value FIELD FLOAT)");
        SqlExecutor.Execute(_db,
            "INSERT INTO meter (time, device, value) VALUES " +
            "(1000, 'm1', 10), (2000, 'm1', 20), (3000, 'm1', 30), (4000, 'm1', 40)");
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    private static SelectExecutionResult Select(Tsdb db, string sql)
        => Assert.IsType<SelectExecutionResult>(SqlExecutor.Execute(db, sql));

    [Fact]
    public void RegisterScalar_ByDelegate_IsResolvedInProjection()
    {
        _db.Functions.RegisterScalar("double_it",
            args => Convert.ToDouble(args[0]) * 2.0,
            minArgumentCount: 1, maxArgumentCount: 1);

        var r = Select(_db, "SELECT time, double_it(value) FROM meter");

        Assert.Equal(4, r.Rows.Count);
        Assert.Equal(20.0, (double)r.Rows[0][1]!, 6);
        Assert.Equal(80.0, (double)r.Rows[3][1]!, 6);
    }

    [Fact]
    public void RegisterScalar_OverridesBuiltin_WithinScope()
    {
        // 内置 abs 取绝对值；用户重写为 +1000 用以检测覆盖路径
        _db.Functions.RegisterScalar("abs",
            args => Convert.ToDouble(args[0]) + 1000.0,
            minArgumentCount: 1, maxArgumentCount: 1);

        var r = Select(_db, "SELECT abs(value) FROM meter");
        Assert.Equal(1010.0, (double)r.Rows[0][0]!, 6);
    }

    [Fact]
    public void RegisterAggregate_FunctionRegistry_RoutesUdfFirst()
    {
        _db.Functions.RegisterAggregate(new SumPlusOneAggregate());

        var r = Select(_db, "SELECT sum_plus_one(value) FROM meter");
        // 总和 100 + 1 = 101
        Assert.Equal(101.0, Convert.ToDouble(r.Rows[0][0]), 6);
    }

    [Fact]
    public void RegisterTableValuedFunction_RoutesAndProducesRows()
    {
        _db.Functions.RegisterTableValuedFunction("repeat",
            (tsdb, statement) =>
            {
                // repeat(meter, n) → 输出 n 行 (idx, 1.0)
                var call = statement.TableValuedFunction!;
                if (call.Arguments[1] is not LiteralExpression lit ||
                    lit.Kind != SqlLiteralKind.Integer)
                    throw new InvalidOperationException("repeat 第 2 个参数必须是整数。");
                int n = (int)lit.IntegerValue;
                var rows = new List<IReadOnlyList<object?>>(n);
                for (int i = 0; i < n; i++)
                    rows.Add(new object?[] { (long)i, 1.0 });
                return new SelectExecutionResult(new[] { "idx", "value" }, rows);
            });

        var r = Select(_db, "SELECT * FROM repeat(meter, 3)");
        Assert.Equal(3, r.Rows.Count);
        Assert.Equal(0L, (long)r.Rows[0][0]!);
        Assert.Equal(2L, (long)r.Rows[2][0]!);
    }

    [Fact]
    public void RegisterTableValuedFunction_NameForecastReserved_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _db.Functions.RegisterTableValuedFunction("forecast",
                (_, _) => new SelectExecutionResult(Array.Empty<string>(), Array.Empty<IReadOnlyList<object?>>())));
    }

    [Fact]
    public void Unregister_RemovesFunction_AndQueryFails()
    {
        _db.Functions.RegisterScalar("triple",
            args => Convert.ToDouble(args[0]) * 3.0, 1, 1);
        Assert.True(_db.Functions.Unregister("triple"));

        Assert.Throws<InvalidOperationException>(() =>
            Select(_db, "SELECT triple(value) FROM meter"));
    }

    [Fact]
    public void DisabledRegistry_RejectsAllRegistrations()
    {
        var path = Path.Combine(_root, "ro");
        Directory.CreateDirectory(path);
        using var ro = Tsdb.Open(new TsdbOptions
        {
            RootDirectory = path,
            AllowUserFunctions = false,
        });

        Assert.False(ro.Functions.IsEnabled);
        Assert.Throws<InvalidOperationException>(() =>
            ro.Functions.RegisterScalar("x", _ => null, 0, 0));
        Assert.Throws<InvalidOperationException>(() =>
            ro.Functions.RegisterAggregate(new SumPlusOneAggregate()));
        Assert.Throws<InvalidOperationException>(() =>
            ro.Functions.RegisterTableValuedFunction("tvf",
                (_, _) => new SelectExecutionResult(Array.Empty<string>(), Array.Empty<IReadOnlyList<object?>>())));
    }

    [Fact]
    public void AmbientScope_IsIsolatedAcrossInstances()
    {
        // 第二个数据库不应看到 _db 上注册的函数
        var path = Path.Combine(_root, "other");
        Directory.CreateDirectory(path);
        using var other = Tsdb.Open(new TsdbOptions { RootDirectory = path });
        SqlExecutor.Execute(other, "CREATE MEASUREMENT m (device TAG, value FIELD FLOAT)");
        SqlExecutor.Execute(other, "INSERT INTO m (time, device, value) VALUES (1000, 'a', 1)");

        _db.Functions.RegisterScalar("zap", _ => 42.0, 0, 0);

        // _db 内可见
        var r1 = Select(_db, "SELECT zap() FROM meter");
        Assert.Equal(42.0, (double)r1.Rows[0][0]!, 6);

        // other 内不可见
        Assert.Throws<InvalidOperationException>(() =>
            Select(other, "SELECT zap() FROM m"));
    }

    [Fact]
    public void RegisterAggregate_RejectsLegacyAggregator()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _db.Functions.RegisterAggregate(new IllegalAggregateWithLegacy()));
    }

    /// <summary>测试用聚合 UDF：sum + 1。</summary>
    private sealed class SumPlusOneAggregate : IAggregateFunction
    {
        public string Name => "sum_plus_one";
        public Aggregator? LegacyAggregator => null;

        public string? ResolveFieldName(FunctionCallExpression call, MeasurementSchema schema)
        {
            var id = (IdentifierExpression)call.Arguments[0];
            return schema.TryGetColumn(id.Name)!.Name;
        }

        public IAggregateAccumulator? CreateAccumulator(FunctionCallExpression call, MeasurementSchema schema)
            => new Accumulator();

        private sealed class Accumulator : IAggregateAccumulator
        {
            private double _sum;
            public long Count { get; private set; }
            public void Add(double value) { _sum += value; Count++; }
            public void Merge(IAggregateAccumulator other)
            {
                var o = (Accumulator)other;
                _sum += o._sum;
                Count += o.Count;
            }
            public object? Finalize() => Count == 0 ? null : (object)(_sum + 1.0);
        }
    }

    private sealed class IllegalAggregateWithLegacy : IAggregateFunction
    {
        public string Name => "bad";
        public Aggregator? LegacyAggregator => Aggregator.Sum;
        public string? ResolveFieldName(FunctionCallExpression call, MeasurementSchema schema) => null;
    }
}
