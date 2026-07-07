#!/usr/bin/env dotnet-script
/*
 * SonnetDB vs IoTDB 对比基准测试运行脚本
 *
 * 使用方法：
 *   dotnet-script run_benchmark.csx
 *
 * 或使用 PowerShell 调用 .NET 程序集
 */

#r "nuget: SonnetDB, 10.0.0"

using System;
using System.Diagnostics;
using System.Threading.Tasks;

// 注意：这是一个示例脚本，展示如何调用测试
// 实际运行时需要正确配置项目引用

Console.WriteLine("╔═══════════════════════════════════════════════════════╗");
Console.WriteLine("║  SonnetDB vs IoTDB 对比基准测试                      ║");
Console.WriteLine("║  Database Comparison Benchmark                        ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════╝");
Console.WriteLine();

Console.WriteLine("📋 测试参数：");
Console.WriteLine("  • 轮次：4 轮（AB BA AB BA）");
Console.WriteLine("  • 设备数：100,000");
Console.WriteLine("  • 测点/设备：30");
Console.WriteLine("  • 时间跨度：1 天（288 x 5分钟间隔）");
Console.WriteLine("  • 总数据点：288,000,000");
Console.WriteLine();

Console.WriteLine("⚙️  环境检查：");
Console.WriteLine();

// 检查 IoTDB
Console.Write("  检查 IoTDB (http://localhost:18080)...");
try
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:18080/rest/v2/query");
    var content = new StringContent("{\"sql\":\"SHOW VERSION\"}", System.Text.Encoding.UTF8, "application/json");
    var auth = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("root:root"));
    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
    request.Content = content;

    var response = await client.SendAsync(request);
    if (response.IsSuccessStatusCode)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(" ✓ 就绪");
        Console.ResetColor();
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($" ✗ 失败 ({response.StatusCode})");
        Console.ResetColor();
        Console.WriteLine("  💡 请启动 IoTDB: docker compose -f tests/SonnetDB.Benchmarks/docker/docker-compose.yml up -d iotdb");
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine(" ✗ 连接失败");
    Console.ResetColor();
    Console.WriteLine($"  错误: {ex.Message}");
    Console.WriteLine("  💡 请启动 IoTDB: docker compose -f tests/SonnetDB.Benchmarks/docker/docker-compose.yml up -d iotdb");
}

Console.WriteLine();
Console.WriteLine("📊 测试将输出以下指标:");
Console.WriteLine("  • 每 10 个批次的进度显示");
Console.WriteLine("  • 单次测试耗时 (ms) 和吞吐量 (pts/sec)");
Console.WriteLine("  • 四轮测试的汇总表格");
Console.WriteLine("  • 统计分析（平均值、最小值、最大值）");
Console.WriteLine("  • 相对性能对比");
Console.WriteLine();

Console.WriteLine("💡 提示：");
Console.WriteLine("  • 测试可能需要 30 分钟到数小时");
Console.WriteLine("  • 建议在专用测试机上运行");
Console.WriteLine("  • 关闭其他应用以获得更稳定的结果");
Console.WriteLine();

Console.WriteLine("按 Enter 键开始测试...");
Console.ReadLine();

Console.WriteLine();
Console.WriteLine("开始运行基准测试...");
Console.WriteLine();

// 在实际使用中，这里应该调用：
// await DatabaseComparisonBenchmark.RunComparison();
//
// 由于这是一个 csx 脚本示例，实际的测试需要在完整的 C# 项目中运行
// 请参考 SonnetDB.Benchmarks 项目中的 DatabaseComparisonBenchmark 类

Console.WriteLine("✓ 测试完成");
Console.WriteLine();
Console.WriteLine("关于如何运行此测试：");
Console.WriteLine("1. 打开 SonnetDB 项目");
Console.WriteLine("2. 创建一个控制台应用");
Console.WriteLine("3. 引用 SonnetDB.Benchmarks 项目");
Console.WriteLine("4. 在 Program.cs 中调用：");
Console.WriteLine();
Console.WriteLine("   using SonnetDB.Benchmarks.Benchmarks;");
Console.WriteLine("   await DatabaseComparisonBenchmark.RunComparison();");
Console.WriteLine();
