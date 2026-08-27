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
        Assert.Contains("LlmProvider", content);
        Assert.Contains("ExecutionContext", content);
        Assert.Contains("AgentContext", content);
        // Not "namespace tools": a namespace cannot carry the index signature the tools object
        // needs, which is why that spelling was the broken one. This asserts the declaration
        // the file actually ships — and TypingsParseTests asserts it compiles.
        Assert.Contains("const tools: OrkeonTools", content, StringComparison.Ordinal);
        Assert.Contains("namespace rag", content);
        Assert.Contains("RagIngestReport", content);
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
