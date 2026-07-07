using SonnetDB.Catalog;
using SonnetDB.Storage.Format;
using Xunit;

namespace SonnetDB.Core.Tests.Catalog;

public class MeasurementSchemaTests
{
    private static MeasurementColumn Field(string name, FieldType type = FieldType.Float64)
        => new(name, MeasurementColumnRole.Field, type);

    private static MeasurementColumn Tag(string name)
        => new(name, MeasurementColumnRole.Tag, FieldType.String);

    [Fact]
    public void Create_WithValidColumns_BuildsSchema()
    {
        var schema = MeasurementSchema.Create("cpu", new MeasurementColumn[]
        {
            Tag("host"),
            Field("usage", FieldType.Float64),
        });

        Assert.Equal("cpu", schema.Name);
        Assert.Equal(2, schema.Columns.Count);
        Assert.NotNull(schema.TryGetColumn("host"));
        Assert.NotNull(schema.TryGetColumn("usage"));
        Assert.Null(schema.TryGetColumn("missing"));
        Assert.Single(schema.TagColumns);
        Assert.Single(schema.FieldColumns);
    }

    [Fact]
    public void Create_WithEmptyColumns_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            MeasurementSchema.Create("cpu", []));
    }

    [Fact]
    public void Create_WithoutAnyField_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            MeasurementSchema.Create("cpu", new[] { Tag("host") }));
    }

    [Fact]
    public void Create_WithDuplicateColumnName_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            MeasurementSchema.Create("cpu", new[]
            {
                Field("a"),
                Field("a"),
            }));
    }

    [Fact]
    public void Create_WithNonStringTag_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            MeasurementSchema.Create("cpu", new[]
            {
                new MeasurementColumn("host", MeasurementColumnRole.Tag, FieldType.Int64),
                Field("usage"),
            }));
    }

    [Fact]
    public void Create_WithUnknownDataType_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            MeasurementSchema.Create("cpu", new[]
            {
                new MeasurementColumn("usage", MeasurementColumnRole.Field, FieldType.Unknown),
            }));
    }

    [Fact]
    public void Create_WithEmptyName_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            MeasurementSchema.Create(" ", new[] { Field("x") }));
    }

    [Fact]
    public void Catalog_Add_RejectsDuplicate()
    {
        var cat = new MeasurementCatalog();
        cat.Add(MeasurementSchema.Create("m", new[] { Field("x") }));
        Assert.Throws<InvalidOperationException>(() =>
            cat.Add(MeasurementSchema.Create("m", new[] { Field("y") })));
        Assert.True(cat.Contains("m"));
        Assert.Equal(1, cat.Count);
    }

    [Fact]
    public void Catalog_TryGet_AfterAdd_ReturnsPublishedSchema()
    {
        var cat = new MeasurementCatalog();
        var schema = MeasurementSchema.Create("cpu", new[] { Field("usage") });

        cat.Add(schema);

        Assert.Same(schema, cat.TryGet("cpu"));
        Assert.True(cat.Contains("cpu"));
        Assert.Equal(1, cat.Count);
    }

    [Fact]
    public void Catalog_LoadOrReplace_AfterSnapshot_PublishesReplacement()
    {
        var cat = new MeasurementCatalog();
        var original = MeasurementSchema.Create("cpu", new[] { Field("usage") });
        var replacement = MeasurementSchema.Create("cpu", new[] { Field("load", FieldType.Int64) });
        cat.Add(original);

        var before = cat.Snapshot();
        cat.LoadOrReplace(replacement);

        Assert.Same(original, Assert.Single(before));
        Assert.Same(replacement, cat.TryGet("cpu"));
        Assert.Equal("load", Assert.Single(cat.Snapshot()).Columns[0].Name);
    }

    [Fact]
    public async Task Catalog_ConcurrentReadsWhileAddingSchemas_AreSafeAndFinalVisible()
    {
        var cat = new MeasurementCatalog();
        var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        using var stop = new System.Threading.CancellationTokenSource();

        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    _ = cat.Snapshot();
                    for (int i = 0; i < 64; i++)
                        _ = cat.TryGet("m" + i);
                }
                catch (Exception ex)
                {
                    errors.Enqueue(ex);
                    break;
                }
            }
        });

        for (int i = 0; i < 64; i++)
            cat.Add(MeasurementSchema.Create("m" + i, new[] { Field("v") }));

        stop.Cancel();
        await reader;

        Assert.Empty(errors);
        Assert.Equal(64, cat.Count);
        for (int i = 0; i < 64; i++)
            Assert.NotNull(cat.TryGet("m" + i));
    }

    [Fact]
    public void Catalog_Snapshot_ReturnsSortedByName()
    {
        var cat = new MeasurementCatalog();
        cat.Add(MeasurementSchema.Create("zeta", new[] { Field("x") }));
        cat.Add(MeasurementSchema.Create("alpha", new[] { Field("x") }));
        cat.Add(MeasurementSchema.Create("mu", new[] { Field("x") }));

        var names = cat.Snapshot().Select(s => s.Name).ToArray();
        Assert.Equal(new[] { "alpha", "mu", "zeta" }, names);
    }
}
