using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// RT-33 - the generated typings document the workflow names <c>edge_kinds</c> and <c>node_kinds</c> take.
/// </summary>
public sealed class GraphKindTypingsTests
{
    private static readonly string[] EdgeNames =
        ["contains", "calls", "imports", "inherits", "tests", "documents", "project_depends"];

    private static readonly string[] NodeNames =
        ["file", "project", "namespace", "type", "member", "doc_section", "test"];

    [Trait("BR", "RT-33")]
    [Fact]
    public async Task ShouldListEveryKindInTypings_WhenGenerated()
    {
        var path = Path.Combine(RepositoryRoot(), "src", "scripting", "Orkeon.Scripting", "Typings", "tools.generated.d.ts");
        var text = (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");

        foreach (var tool in new[] { "dependency_graph", "sub_graph" })
        {
            var start = text.IndexOf($"/** `{tool}`", StringComparison.Ordinal);
            Assert.True(start >= 0, $"{tool} is missing from the typings");
            var block = text[start..text.IndexOf("}>;", start, StringComparison.Ordinal)];

            Assert.Contains("node_kinds", block);
            foreach (var name in EdgeNames.Concat(NodeNames))
                Assert.True(
                    Regex.IsMatch(block, $@"(?<!\w){Regex.Escape(name)}(?!\w)"),
                    $"{tool} typings do not mention the kind name '{name}'.");
        }
    }

    [Trait("BR", "RT-33")]
    [Fact]
    public async Task ShouldDeclareKindsOptional_WhenTypingsGenerated()
    {
        var path = Path.Combine(RepositoryRoot(), "src", "scripting", "Orkeon.Scripting", "Typings", "tools.generated.d.ts");
        var text = (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");

        foreach (var tool in new[] { "dependency_graph", "sub_graph" })
        {
            var start = text.IndexOf($"/** `{tool}`", StringComparison.Ordinal);
            Assert.True(start >= 0, $"{tool} is missing from the typings");
            var block = text[start..text.IndexOf("}>;", start, StringComparison.Ordinal)];

            foreach (var name in new[] { "edge_kinds", "node_kinds" })
                Assert.True(
                    Regex.IsMatch(block, $@"(?<!\w){name}\?:"),
                    $"{tool} typings do not declare '{name}' optional.");
        }
    }

    private static string RepositoryRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
}
