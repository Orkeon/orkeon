using Orkeon.Scripting.Bindings;

namespace Orkeon.Hosting.Tests;

/// <summary>RT-31 - the structural rule schema is published in the generated typings.</summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class StructuralRuleTypingsTests : IDisposable
{
    private readonly string _root = RegisteredTools.CreateRoot("orkeon-rule-typings-");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    [Fact]
    [Trait("BR", "RT-31")]
    public async Task ShouldPublishRuleSchemaInTypings_WhenGenerated()
    {
        var rendered = BuiltInToolTypings.Render(await RegisteredTools.ReadAsync(_root)).ReplaceLineEndings("\n");

        var start = rendered.IndexOf("structuralSearch(", StringComparison.Ordinal);
        Assert.True(start >= 0, "structural_search is not typed.");
        var end = rendered.IndexOf("Promise<", start, StringComparison.Ordinal);
        Assert.True(end > start, "structural_search declaration has no return type.");
        var line = rendered[start..end];
        foreach (var member in new[] { "pattern?:", "language", "paths?:", "glob?:", "max_results?:", "max_files?:", "rule?:", "rule_file?: string" })
            Assert.Contains(member, line, StringComparison.Ordinal);
        Assert.Contains("kind, pattern, regex, field, inside, has, not, all, any, stopBy", rendered, StringComparison.Ordinal);
    }
}
