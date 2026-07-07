using System.Collections.Concurrent;
using SonnetDB.Catalog;
using Xunit;

namespace SonnetDB.Core.Tests.Catalog;

/// <summary>
/// Tag 倒排索引相关行为测试（PR #27）。
/// 主要覆盖：单 tag 等值、多 tag 交集、未命中、缺失 tagKey、measurement 隔离、并发安全、
/// 通过 <see cref="SeriesCatalog.LoadEntry"/> 路径重建索引。
/// </summary>
public sealed class TagInvertedIndexTests
{
    private static SeriesCatalog NewCatalog() => new();

    private static IReadOnlyDictionary<string, string> Tags(params (string k, string v)[] kv)
    {
        var d = new Dictionary<string, string>(kv.Length);
        foreach (var (k, v) in kv) d[k] = v;
        return d;
    }

    [Fact]
    public void Find_NoTagFilter_ReturnsAllForMeasurement()
    {
        var c = NewCatalog();
        c.GetOrAdd("cpu", Tags(("host", "a")));
        c.GetOrAdd("cpu", Tags(("host", "b")));
        c.GetOrAdd("mem", Tags(("host", "a")));

        var hits = c.Find("cpu", null);
        Assert.Equal(2, hits.Count);
        Assert.All(hits, e => Assert.Equal("cpu", e.Measurement));
    }

    [Fact]
    public void Find_SingleTag_ReturnsOnlyMatching()
    {
        var c = NewCatalog();
        var a = c.GetOrAdd("cpu", Tags(("host", "a"), ("dc", "x")));
        c.GetOrAdd("cpu", Tags(("host", "b"), ("dc", "x")));
        c.GetOrAdd("cpu", Tags(("host", "a"), ("dc", "y")));

        var hits = c.Find("cpu", Tags(("dc", "y")));
        Assert.Single(hits);
        Assert.Equal("y", hits[0].Tags["dc"]);

        var hitsHostA = c.Find("cpu", Tags(("host", "a")));
        Assert.Equal(2, hitsHostA.Count);
        Assert.Contains(hitsHostA, e => e.Id == a.Id);
    }

    [Fact]
    public void Find_MultiTag_ReturnsIntersection()
    {
        var c = NewCatalog();
        var target = c.GetOrAdd("cpu", Tags(("host", "a"), ("dc", "x"), ("env", "prod")));
        c.GetOrAdd("cpu", Tags(("host", "a"), ("dc", "x"), ("env", "dev")));
        c.GetOrAdd("cpu", Tags(("host", "b"), ("dc", "x"), ("env", "prod")));

        var hits = c.Find("cpu", Tags(("host", "a"), ("env", "prod")));
        Assert.Single(hits);
        Assert.Equal(target.Id, hits[0].Id);
    }

    [Fact]
    public void Find_NonMatchingValue_ReturnsEmpty()
    {
        var c = NewCatalog();
        c.GetOrAdd("cpu", Tags(("host", "a")));
        Assert.Empty(c.Find("cpu", Tags(("host", "ghost"))));
    }

    [Fact]
    public void Find_MissingTagKey_ReturnsEmpty()
    {
        var c = NewCatalog();
        c.GetOrAdd("cpu", Tags(("host", "a")));
        Assert.Empty(c.Find("cpu", Tags(("never", "value"))));
    }

    [Fact]
    public void Find_UnknownMeasurement_ReturnsEmpty()
    {
        var c = NewCatalog();
        c.GetOrAdd("cpu", Tags(("host", "a")));
        Assert.Empty(c.Find("disk", null));
        Assert.Empty(c.Find("disk", Tags(("host", "a"))));
    }

    [Fact]
    public void Find_MeasurementIsolation_NoCrossLeak()
    {
        var c = NewCatalog();
        c.GetOrAdd("cpu", Tags(("host", "a")));
        c.GetOrAdd("mem", Tags(("host", "a")));
        var hits = c.Find("cpu", Tags(("host", "a")));
        Assert.Single(hits);
        Assert.Equal("cpu", hits[0].Measurement);
    }

    [Fact]
    public void Find_AfterClear_ReturnsEmpty()
    {
        var c = NewCatalog();
        c.GetOrAdd("cpu", Tags(("host", "a")));
        c.Clear();
        Assert.Empty(c.Find("cpu", null));
        Assert.Empty(c.Find("cpu", Tags(("host", "a"))));
    }

    [Fact]
    public void Find_DuplicateGetOrAdd_DoesNotInflateIndex()
    {
        var c = NewCatalog();
        for (int i = 0; i < 10; i++)
            c.GetOrAdd("cpu", Tags(("host", "a")));
        var hits = c.Find("cpu", Tags(("host", "a")));
        Assert.Single(hits);
    }

    [Fact]
    public void Find_AfterAdditionalAdd_PublishesUpdatedFrozenSnapshot()
    {
        var c = NewCatalog();
        var first = c.GetOrAdd("cpu", Tags(("host", "a"), ("rack", "r1")));

        Assert.Same(first, Assert.Single(c.Find("cpu", Tags(("rack", "r1")))));
        Assert.Empty(c.Find("cpu", Tags(("host", "b"))));

        var second = c.GetOrAdd("cpu", Tags(("host", "b"), ("rack", "r1")));

        var rackHits = c.Find("cpu", Tags(("rack", "r1")));
        Assert.Equal(2, rackHits.Count);
        Assert.Contains(rackHits, e => e.Id == first.Id);
        Assert.Contains(rackHits, e => e.Id == second.Id);
        Assert.Same(second, Assert.Single(c.Find("cpu", Tags(("host", "b")))));
    }

    [Fact]
    public void LoadEntry_RebuildsTagIndex()
    {
        var src = NewCatalog();
        src.GetOrAdd("cpu", Tags(("host", "a"), ("dc", "x")));
        src.GetOrAdd("cpu", Tags(("host", "b"), ("dc", "x")));

        // 模拟 CatalogFileCodec 在新实例上 LoadEntry 重放
        var dst = NewCatalog();
        foreach (var entry in src.Snapshot())
            typeof(SeriesCatalog)
                .GetMethod("LoadEntry", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(dst, new object[] { entry });

        var hits = dst.Find("cpu", Tags(("dc", "x")));
        Assert.Equal(2, hits.Count);
        var hostA = dst.Find("cpu", Tags(("host", "a")));
        Assert.Single(hostA);
    }

    [Fact]
    public async Task Find_ConcurrentWritesAndReads_AreSafe()
    {
        var c = NewCatalog();
        const int writers = 4;
        const int perWriter = 200;

        var tasks = new List<Task>();
        for (int w = 0; w < writers; w++)
        {
            int id = w;
            tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < perWriter; i++)
                {
                    c.GetOrAdd("cpu", Tags(("host", "h" + id), ("idx", i.ToString())));
                }
            }));
        }
        // 并发读
        var stopper = new ManualResetEventSlim();
        var reader = Task.Run(() =>
        {
            while (!stopper.IsSet)
            {
                _ = c.Find("cpu", Tags(("host", "h0")));
                _ = c.Find("cpu", null);
            }
        });
        await Task.WhenAll(tasks);
        stopper.Set();
        await reader;

        Assert.Equal(writers * perWriter, c.Count);
        var hostHits = c.Find("cpu", Tags(("host", "h1")));
        Assert.Equal(perWriter, hostHits.Count);
    }
}
