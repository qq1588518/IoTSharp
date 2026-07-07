## 用户自定义函数：使用 RegisterScalar、RegisterAggregate 等扩展 SonnetDB 分析能力

SonnetDB 内置了 50 多种标量和聚合函数，但在实际业务场景中，您通常需要特定领域的计算逻辑。为此，SonnetDB 提供了完整的用户自定义函数（UDF）框架，支持注册标量函数、聚合函数、窗口函数和表值函数四类扩展，全部通过 C# 代码实现。

### 注册标量函数（RegisterScalar）

标量函数接收一个或多个输入值，返回单个计算结果。下面我们注册一个计算设备健康指数的自定义标量函数：

```csharp
using SonnetDB.Data.Udf;

public class DeviceHealthFunction : IScalarFunction
{
    public string Name => "device_health";
    
    public object? Invoke(object?[] args)
    {
        // 参数：temperature, vibration, uptime_hours
        double temp = Convert.ToDouble(args[0]);
        double vibration = Convert.ToDouble(args[1]);
        double uptime = Convert.ToDouble(args[2]);
        
        // 健康指数 = 100 - 温度罚分 - 振动罚分 + 运行时长奖励
        double score = 100.0
            - Math.Max(0, (temp - 50.0) * 0.5)   // 超过50°C开始扣分
            - vibration * 2.0                      // 振动越大扣分越多
            + Math.Min(10, uptime / 1000.0);       // 稳定运行奖励
        
        return Math.Clamp(score, 0, 100);
    }
}

// 注册标量函数
SndbUdfManager.RegisterScalar(new DeviceHealthFunction());

// 在 SQL 中使用
// SELECT device_id, time, device_health(temperature, vibration, uptime_hours)
// FROM machine_metrics WHERE time > 1713676800000
```

### 注册聚合函数（RegisterAggregate）

聚合函数对一组数据行进行归约计算，返回单个汇总值。下面的示例实现了一个自定义的"加权平均"聚合函数：

```csharp
public class WeightedAverageFunction : IAggregateFunction
{
    public string Name => "wavg";
    
    public object? Aggregate(IEnumerable<object?[]> values)
    {
        double sum = 0, weightSum = 0;
        
        foreach (var row in values)
        {
            double value = Convert.ToDouble(row[0]);
            double weight = Convert.ToDouble(row[1]);
            sum += value * weight;
            weightSum += weight;
        }
        
        return weightSum > 0 ? sum / weightSum : 0;
    }
}

// 注册聚合函数
SndbUdfManager.RegisterAggregate(new WeightedAverageFunction());

// SQL 使用：计算交易量加权平均价格
// SELECT symbol, wavg(price, volume) AS vwap
// FROM stock_ticks WHERE time >= 1713676800000 GROUP BY symbol
```

### 注册窗口函数（RegisterWindow）

窗口函数在滑动时间窗口上执行计算，特别适合时序数据分析中的滑动平均、累积和等模式：

```csharp
public class RangeFunction : IWindowFunction
{
    public string Name => "range_normalize";
    
    public object?[] Apply(IEnumerable<object?[]> window, string[] columns)
    {
        var values = window.Select(r => Convert.ToDouble(r[0])).ToArray();
        double min = values.Min();
        double max = values.Max();
        double range = max - min;
        
        if (range == 0) return values.Select(v => (object?)0.0).ToArray();
        return values.Select(v => (object?)((v - min) / range)).ToArray();
    }
}

SndbUdfManager.RegisterWindow(new RangeFunction());

// SQL 使用：对传感器读数做范围归一化
// SELECT time, range_normalize(temperature) OVER (ORDER BY time ROWS 10 PRECEDING)
// FROM sensor_data WHERE device_id = 'sensor-01'
```

### 注册表值函数（RegisterTableValuedFunction）

表值函数返回一个数据表，可以像普通表一样在 `FROM` 子句中使用。这在需要生成模拟数据或实现复杂转换时非常有用：

```csharp
public class GenerateSeriesFunction : ITableValuedFunction
{
    public string Name => "generate_series";
    
    public IEnumerable<object?[]> Invoke(object?[] args)
    {
        long start = Convert.ToInt64(args[0]);
        long end = Convert.ToInt64(args[1]);
        long step = args.Length > 2 ? Convert.ToInt64(args[2]) : 1;
        
        for (long i = start; i <= end; i += step)
        {
            yield return new object?[] { i, $"value_{i}" };
        }
    }
}

SndbUdfManager.RegisterTableValuedFunction(new GenerateSeriesFunction());

// SQL 使用：生成模拟时序数据
// SELECT * FROM generate_series(1, 10, 2);
```

总结而言，SonnetDB 的 UDF 框架提供了极大的灵活性。通过 `RegisterScalar`、`RegisterAggregate`、`RegisterWindow` 和 `RegisterTableValuedFunction` 四个核心接口，开发者可以将任意业务逻辑无缝融入 SQL 查询中，构建高度定制化的时序数据分析管道。
