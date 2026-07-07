using SonnetDB.Copilot;
using Xunit;

namespace SonnetDB.Tests.Copilot;

public sealed class PromptTemplatesTests
{
    [Fact]
    public void Load_SqlGen_ReturnsEmbeddedTemplate()
    {
        var text = PromptTemplates.Load("sql-gen");
        Assert.Contains("SonnetDB", text);
        Assert.Contains("CREATE MEASUREMENT", text);
        Assert.Contains("{{db}}", text);
        Assert.Contains("{{measurements}}", text);
    }

    [Fact]
    public void Load_SqlGenNoDb_ReturnsEmbeddedTemplate()
    {
        var text = PromptTemplates.Load("sql-gen-no-db");
        Assert.Contains("SonnetDB", text);
        Assert.Contains("knn(", text);
        Assert.DoesNotContain("{{db}}", text);
    }

    [Fact]
    public void Render_SubstitutesPlaceholders()
    {
        var result = PromptTemplates.Render("sql-gen", new Dictionary<string, string>
        {
            ["db"] = "metrics",
            ["measurements"] = "- cpu (time, host TAG, usage FIELD FLOAT)",
        });

        Assert.Contains("\"metrics\"", result);
        Assert.Contains("- cpu (time, host TAG, usage FIELD FLOAT)", result);
        Assert.DoesNotContain("{{db}}", result);
        Assert.DoesNotContain("{{measurements}}", result);
    }

    [Fact]
    public void Load_Unknown_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => PromptTemplates.Load("does-not-exist"));
        Assert.Contains("does-not-exist", ex.Message);
    }
}
