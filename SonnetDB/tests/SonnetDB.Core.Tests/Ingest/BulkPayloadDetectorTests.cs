using SonnetDB.Ingest;
using Xunit;

namespace SonnetDB.Core.Tests.Ingest;

public sealed class BulkPayloadDetectorTests
{
    [Fact]
    public void Detect_LineProtocol_ReturnsLineProtocol()
    {
        var fmt = BulkPayloadDetector.Detect("cpu,host=srv1 value=0.5 1700000000000".AsSpan());
        Assert.Equal(BulkPayloadFormat.LineProtocol, fmt);
    }

    [Fact]
    public void Detect_Json_ReturnsJson()
    {
        var fmt = BulkPayloadDetector.Detect("  {\"m\":\"cpu\",\"points\":[]}  ".AsSpan());
        Assert.Equal(BulkPayloadFormat.Json, fmt);
    }

    [Theory]
    [InlineData("INSERT INTO cpu(host,value) VALUES ('a',1)")]
    [InlineData("  insert into cpu(value) values (1)  ")]
    public void Detect_BulkValues_ReturnsBulkValues(string s)
    {
        Assert.Equal(BulkPayloadFormat.BulkValues, BulkPayloadDetector.Detect(s.AsSpan()));
    }

    [Fact]
    public void Detect_Empty_DefaultsToLineProtocol()
    {
        Assert.Equal(BulkPayloadFormat.LineProtocol, BulkPayloadDetector.Detect("".AsSpan()));
        Assert.Equal(BulkPayloadFormat.LineProtocol, BulkPayloadDetector.Detect("   \n  ".AsSpan()));
    }

    [Fact]
    public void DetectWithPrefix_FirstLineIsMeasurement_StripsPrefix()
    {
        const string text = "sensor_data\nsensor_data,host=a value=1 1700000000000";
        var fmt = BulkPayloadDetector.DetectWithPrefix(text, out var m, out var payload);
        Assert.Equal(BulkPayloadFormat.LineProtocol, fmt);
        Assert.Equal("sensor_data", m);
        Assert.Equal("sensor_data,host=a value=1 1700000000000", payload.ToString());
    }

    [Fact]
    public void DetectWithPrefix_NoPrefix_ReturnsNullMeasurement()
    {
        const string text = "cpu,host=a value=1 1700000000000";
        var fmt = BulkPayloadDetector.DetectWithPrefix(text, out var m, out var payload);
        Assert.Equal(BulkPayloadFormat.LineProtocol, fmt);
        Assert.Null(m);
        Assert.Equal(text, payload.ToString());
    }
}
