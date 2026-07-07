using System.Linq;
using SonnetDB.Copilot;
using Xunit;

namespace SonnetDB.Tests;

public sealed class CopilotProvisioningTests
{
    [Fact]
    public void LooksLikeProvisioningRequest_WithCreateWarehouseLanguage_ReturnsTrue()
    {
        var result = CopilotProvisioning.LooksLikeProvisioningRequest("帮我新建一个保存电脑性能数据的仓库");

        Assert.True(result);
    }

    [Fact]
    public void TryExtractIntent_ComputerPerformanceRequest_ExtractsDefaultDatabaseMeasurementAndFields()
    {
        var intent = CopilotProvisioning.TryExtractIntent(
            "帮我新建一个保存电脑性能数据的仓库，把 cpu 内存 的使用率以及 cpu 的温度建成表。");

        Assert.NotNull(intent);
        Assert.Equal("computer_perf", intent!.DatabaseName);
        Assert.Equal("host_perf", intent.MeasurementName);
        Assert.True(intent.ExecuteNow);

        var tag = Assert.Single(intent.Tags);
        Assert.Equal("host", tag.Name);
        Assert.True(tag.IsTag);
        Assert.Equal("STRING", tag.Type);

        Assert.Equal(
            ["cpu_usage", "memory_usage", "cpu_temp_celsius"],
            intent.Fields.Select(static field => field.Name).ToArray());
        Assert.All(intent.Fields, static field => Assert.Equal("FLOAT", field.Type));
    }

    [Fact]
    public void TryExtractIntent_WithExplicitDatabaseName_UsesExplicitName()
    {
        var intent = CopilotProvisioning.TryExtractIntent(
            "帮我新建一个仓库 perfmetrics_exec，用来保存电脑性能数据，把 cpu 内存 的使用率以及 cpu 的温度建成表。");

        Assert.NotNull(intent);
        Assert.Equal("perfmetrics_exec", intent!.DatabaseName);
    }

    [Fact]
    public void TryExtractIntent_WhenPromptAsksForSqlOnly_DoesNotExecuteImmediately()
    {
        var intent = CopilotProvisioning.TryExtractIntent(
            "帮我写 sql 语句，新建一个仓库 perfmetrics_draft，用来保存电脑性能数据，把 cpu 内存 的使用率以及 cpu 的温度建成表。");

        Assert.NotNull(intent);
        Assert.False(intent!.ExecuteNow);
    }

    [Fact]
    public void BuildCreateMeasurementSql_IncludesTagAndAllExtractedFields()
    {
        var intent = CopilotProvisioning.TryExtractIntent(
            "帮我新建一个仓库 perfmetrics_exec，用来保存电脑性能数据，把 cpu 内存 的使用率以及 cpu 的温度建成表。")
            ?? throw new Xunit.Sdk.XunitException("Expected provisioning intent.");

        var sql = CopilotProvisioning.BuildCreateMeasurementSql(intent);

        Assert.Equal(
            "CREATE MEASUREMENT host_perf (host TAG, cpu_usage FIELD FLOAT, memory_usage FIELD FLOAT, cpu_temp_celsius FIELD FLOAT)",
            sql);
    }
}
