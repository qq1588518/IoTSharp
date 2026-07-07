namespace SonnetDB.Accuracy.Tests;

internal static class AccuracyDataSet
{
    public const string DatabaseName = "accuracy";
    public const string InfluxOrg = "sndb";
    public const string InfluxBucket = "accuracy";
    public const string InfluxAdminToken = "accuracy-super-secret-token";
    public const string SonnetAdminToken = "accuracy-admin-token";

    public static readonly DateTimeOffset RangeStart = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset RangeStop = new(2024, 1, 1, 0, 6, 0, TimeSpan.Zero);

    public static IReadOnlyList<MeasurementSeed> Measurements { get; } =
    [
        new MeasurementSeed(
            "telemetry",
            "CREATE MEASUREMENT telemetry (host TAG, region TAG, usage FIELD FLOAT, errors FIELD INT, active FIELD BOOL, status FIELD STRING)",
            """
            telemetry,host=alpha,region=cn usage=1.5,errors=2i,active=true,status="ok" 1704067200000
            telemetry,host=alpha,region=cn usage=2.5,errors=3i,active=false,status="warn" 1704067260000
            telemetry,host=alpha,region=cn usage=3.75,errors=5i,active=true,status="ok" 1704067320000
            telemetry,host=beta,region=us usage=10.0,errors=1i,active=true,status="ok" 1704067200000
            telemetry,host=beta,region=us usage=11.0,active=true,status="ok" 1704067260000
            telemetry,host=beta,region=us errors=4i,status="fail" 1704067320000
            telemetry,host=beta,region=us usage=12.0,errors=6i,active=false,status="fail" 1704067380000
            """),
        new MeasurementSeed(
            "audit",
            "CREATE MEASUREMENT audit (host TAG, severity TAG, code FIELD INT, message FIELD STRING)",
            """
            audit,host=alpha,severity=info code=100i,message="started" 1704067200000
            audit,host=beta,severity=warn code=200i,message="retry" 1704067260000
            """),
    ];

    public static string RangeStartRfc3339 => ToRfc3339(RangeStart);

    public static string RangeStopRfc3339 => ToRfc3339(RangeStop);

    private static string ToRfc3339(DateTimeOffset value)
        => value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
}

internal sealed record MeasurementSeed(
    string Name,
    string CreateMeasurementSql,
    string LineProtocol);
