using SonnetDB.Model;
using SonnetDB.Query.Functions;
using SonnetDB.Query.Functions.Control;
using Xunit;

namespace SonnetDB.Core.Tests.Query.Functions.Control;

/// <summary>
/// PR #54 — <see cref="PidController"/> 与 PID SQL 函数累加器/求值器的纯算法单元测试。
/// </summary>
public sealed class PidControllerTests
{
    [Fact]
    public void Update_FirstSample_OutputsProportionalOnly()
    {
        var pid = new PidController(kp: 2.0, ki: 0.5, kd: 0.1);
        // setpoint=10, pv=4 → e=6 → u = 2*6 = 12（首行没有 dt，I/D 为 0）
        double u = pid.Update(0L, processVariable: 4.0, setpoint: 10.0);
        Assert.Equal(12.0, u, precision: 9);
        Assert.True(pid.HasHistory);
    }

    [Fact]
    public void Update_SecondSample_AccumulatesIntegralAndDerivative()
    {
        var pid = new PidController(kp: 1.0, ki: 1.0, kd: 1.0);
        // 第一行 ts=0 pv=0 setpoint=10 → e0=10，u0=10
        pid.Update(0L, 0.0, 10.0);
        // 第二行 ts=1000ms pv=4 setpoint=10 → e1=6，dt=1s
        // integral = e1 * dt = 6
        // derivative = (6-10)/1 = -4
        // u = 1*6 + 1*6 + 1*(-4) = 8
        double u = pid.Update(1000L, 4.0, 10.0);
        Assert.Equal(8.0, u, precision: 9);
        Assert.Equal(6.0, pid.Integral, precision: 9);
        Assert.Equal(6.0, pid.PrevError, precision: 9);
    }

    [Fact]
    public void Update_ZeroDt_DoesNotMutateState()
    {
        var pid = new PidController(kp: 1.0, ki: 1.0, kd: 1.0);
        pid.Update(1000L, 0.0, 10.0); // 初始化 prevTime=1000, prevError=10
        var snap1 = pid.Snapshot();

        // 同一时间戳：跳过 I/D 累计，只输出 P + Ki*integral（积分项为 0）
        double u = pid.Update(1000L, 5.0, 10.0);
        Assert.Equal(5.0, u, precision: 9); // Kp*5 = 5
        Assert.Equal(snap1, pid.Snapshot()); // 状态未变
    }

    [Fact]
    public void Reset_ClearsHistory()
    {
        var pid = new PidController(1, 1, 1);
        pid.Update(0L, 0, 10);
        pid.Update(1000L, 5, 10);
        pid.Reset();

        Assert.False(pid.HasHistory);
        Assert.Equal(0.0, pid.Integral);
        Assert.Equal(0.0, pid.PrevError);
        Assert.Equal(0L, pid.PrevTimeMs);
    }

    [Fact]
    public void SnapshotRestore_RoundTripsState()
    {
        var a = new PidController(1, 1, 1);
        a.Update(0L, 0, 10);
        a.Update(1000L, 5, 10);
        var snap = a.Snapshot();

        var b = new PidController(1, 1, 1);
        b.Restore(snap);

        Assert.Equal(snap, b.Snapshot());
        // 接续推进得到相同输出
        double ua = a.Update(2000L, 8, 10);
        double ub = b.Update(2000L, 8, 10);
        Assert.Equal(ua, ub, precision: 9);
    }

    // ── PidSeriesEvaluator ───────────────────────────────────────────────

    [Fact]
    public void PidSeriesEvaluator_OutputsControlSignalPerRow()
    {
        var ev = new PidSeriesEvaluator("pv", setpoint: 10.0, kp: 1.0, ki: 1.0, kd: 1.0);
        var ts = new long[] { 0, 1000, 2000 };
        var values = new FieldValue?[]
        {
            FieldValue.FromDouble(0.0),
            FieldValue.FromDouble(4.0),
            FieldValue.FromDouble(7.0),
        };

        var u = ev.Compute(ts, values);

        // 行 0: u = 10
        Assert.Equal(10.0, (double)u[0]!, precision: 9);
        // 行 1: e=6, dt=1, integral=6, derivative=-4, u = 6+6-4 = 8
        Assert.Equal(8.0, (double)u[1]!, precision: 9);
        // 行 2: e=3, dt=1, integral=6+3=9, derivative=(3-6)/1=-3, u = 3+9-3 = 9
        Assert.Equal(9.0, (double)u[2]!, precision: 9);
    }

    [Fact]
    public void PidSeriesEvaluator_NullValuesProduceNullOutputs()
    {
        var ev = new PidSeriesEvaluator("pv", setpoint: 10.0, kp: 1.0, ki: 0.0, kd: 0.0);
        var ts = new long[] { 0, 1000, 2000 };
        var values = new FieldValue?[]
        {
            FieldValue.FromDouble(0.0),
            null,
            FieldValue.FromDouble(5.0),
        };

        var u = ev.Compute(ts, values);
        Assert.Equal(10.0, (double)u[0]!, precision: 9);
        Assert.Null(u[1]);
        Assert.NotNull(u[2]);
    }

    // ── PidAccumulator ───────────────────────────────────────────────────

    [Fact]
    public void PidAccumulator_FinalizesLastControlSignal()
    {
        var acc = new PidAccumulator(setpoint: 10.0, kp: 1.0, ki: 1.0, kd: 1.0);
        acc.Add(0L, 0.0);     // u=10
        acc.Add(1000L, 4.0);  // u=8
        acc.Add(2000L, 7.0);  // u=9

        Assert.Equal(3, acc.Count);
        Assert.Equal(9.0, (double)acc.Finalize()!, precision: 9);
    }

    [Fact]
    public void PidAccumulator_Empty_FinalizesNull()
    {
        var acc = new PidAccumulator(0, 1, 0, 0);
        Assert.Null(acc.Finalize());
    }

    [Fact]
    public void PidAccumulator_Merge_TakesLaterTimestampedState()
    {
        var early = new PidAccumulator(10, 1, 0, 0);
        early.Add(0L, 0);
        early.Add(1000L, 5); // last u = 5

        var late = new PidAccumulator(10, 1, 0, 0);
        late.Add(2000L, 7); // last u = 3

        early.Merge(late);
        Assert.Equal(3.0, (double)early.Finalize()!, precision: 9);
        Assert.Equal(3, early.Count);
    }

    [Fact]
    public void PidAccumulator_MergeIntoEmpty_AdoptsOtherState()
    {
        var empty = new PidAccumulator(10, 1, 0, 0);
        var other = new PidAccumulator(10, 1, 0, 0);
        other.Add(1000L, 5); // u = 5

        empty.Merge(other);
        Assert.Equal(5.0, (double)empty.Finalize()!, precision: 9);
        Assert.Equal(1, empty.Count);
    }

    // ── FunctionRegistry 注册校验 ─────────────────────────────────────────

    [Fact]
    public void FunctionRegistry_RegistersPidAggregateAndWindow()
    {
        Assert.Equal(FunctionKind.Aggregate, FunctionRegistry.GetFunctionKind("pid"));
        Assert.Equal(FunctionKind.Window, FunctionRegistry.GetFunctionKind("pid_series"));
        Assert.Equal(FunctionKind.Aggregate, FunctionRegistry.GetFunctionKind("pid_estimate"));

        Assert.True(FunctionRegistry.TryGetAggregate("pid", out var agg));
        Assert.Equal("pid", agg.Name);

        Assert.True(FunctionRegistry.TryGetWindow("pid_series", out var win));
        Assert.Equal("pid_series", win.Name);

        Assert.True(FunctionRegistry.TryGetAggregate("pid_estimate", out var est));
        Assert.Equal("pid_estimate", est.Name);
    }
}
