namespace Orkeon.Scripting.Tests.Typings;

public sealed class RollupDtsTests
{
    [Fact]
    public void Rollup_dts_concatenates_all_modules()
    {
        var rollupPath = ResolveRollupPath();
        Assert.SkipWhen(rollupPath is null, "Rollup orkeon.d.ts not found relative to the test bin directory.");

        var content = File.ReadAllText(rollupPath!);

        Assert.Contains("Orkeon Scripting DSL v1.0", content);
        Assert.Contains("ScriptVersionMismatchError", content);
        Assert.Contains("AgentBuilder", content);
        Assert.Contains("CrewBuilder", content);
        Assert.Contains("TaskBuilder", content);
        Assert.Contains("ToolBuilder", content);
        Assert.Contains("EventQueue", content);
        Assert.Contains("EventTopic", content);
        Assert.Contains("StateMachine", content);
        Assert.Contains("StateGraph", content);
        Assert.Contains("LlmConfigOverrides", content);
        Assert.Contains("ExecutionContext", content);
        Assert.Contains("AgentContext", content);
        // Not "namespace tools": a namespace cannot carry the index signature the tools object
        // needs, which is why that spelling was the broken one. This asserts the declaration
        // the file actually ships — and TypingsParseTests asserts it compiles.
        Assert.Contains("const tools: OrkeonTools", content, StringComparison.Ordinal);
        Assert.Contains("namespace rag", content);
        Assert.Contains("RagIngestReport", content);
    }

    /// <summary>
    /// #288: the roll-up embedded in the assembly carries the typings byte for byte. When it went
    /// through MSBuild items, the backslash of a doc comment's <c>*\/</c> escape became a slash on
    /// Linux, and <c>*//</c> closed the comment early: tsc refused the file.
    /// </summary>
    [Fact]
    public void Embedded_rollup_keeps_doc_comment_escapes()
    {
        using var stream = typeof(ScriptHost).Assembly.GetManifestResourceStream("Orkeon.Scripting.Typings.orkeon.d.ts");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var lines = reader.ReadToEnd().Split('\n');

        Assert.DoesNotContain(lines, line => line.Contains("*//", StringComparison.Ordinal));
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("/**", StringComparison.Ordinal)) continue;
            var close = trimmed.IndexOf("*/", 2, StringComparison.Ordinal);
            Assert.True(close < 0 || close == trimmed.Length - 2, $"A doc comment closes before its end: {trimmed}");
        }
        Assert.Contains(lines, line => line.Contains("**\\/*.cs", StringComparison.Ordinal));
    }

    private static string? ResolveRollupPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName, "src", "scripting", "Orkeon.Scripting", "bin", "Debug", "net10.0", "dist", "orkeon.d.ts");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
