using Orkeon.Scripting.Toolchain;

namespace Orkeon.Scripting.Tests.Typings;

/// <summary>
/// The roll-up <c>orkeon.d.ts</c> the build embeds in <c>Orkeon.Scripting</c>: what
/// <c>orkeon typings</c> writes next to a project.
/// </summary>
public sealed class RollupDtsTests
{
    private const string ResourceName = "Orkeon.Scripting.Typings.orkeon.d.ts";
    private const int BannerLines = 3;

    [Fact]
    public void Rollup_dts_concatenates_all_modules()
    {
        var content = ReadEmbeddedRollup();

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
        // the file actually ships, and Embedded_rollup_parses_as_typescript asserts it parses.
        Assert.Contains("const tools: OrkeonTools", content, StringComparison.Ordinal);
        Assert.Contains("namespace rag", content);
        Assert.Contains("RagIngestReport", content);
    }

    /// <summary>
    /// #288: the roll-up carries the typings byte for byte, errors.d.ts first then the rest in
    /// ordinal order. When the lines went through MSBuild items, the backslash of a doc
    /// comment's <c>*\/</c> escape became a slash on Linux and <c>*//</c> closed the comment
    /// early: tsc refused the file.
    /// </summary>
    [Fact]
    public void Embedded_rollup_is_the_sources_byte_for_byte()
    {
        var content = ReadEmbeddedRollup();

        var sources = Directory.EnumerateFiles(TypingsParseTests.TypingsDirectory(), "*.d.ts")
            .Order(StringComparer.Ordinal)
            .OrderBy(file => Path.GetFileName(file) == "errors.d.ts" ? 0 : 1)
            .ToList();
        var expected = string.Concat(sources.Select(file =>
        {
            var text = File.ReadAllText(file);
            return text.Length > 0 && !text.EndsWith('\n') ? text + "\n" : text;
        }));

        var body = string.Join("\n", content.Split('\n').Skip(BannerLines));
        Assert.Equal(expected, body);
        Assert.DoesNotContain("*//", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// #288 through the parser: the per-file theory of <see cref="TypingsParseTests"/> parses the
    /// sources, not the roll-up, so a roll-up that broke them was invisible to it.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task Embedded_rollup_parses_as_typescript()
    {
        var source = ReadEmbeddedRollup();

        using var transpiler = new EsbuildTranspiler();
        try
        {
            await transpiler.TranspileAsync(source, TestContext.Current.CancellationToken);
        }
        catch (EsbuildNotFoundException)
        {
            Assert.Skip("esbuild is not available in this environment.");
        }
        catch (EsbuildTranspileException ex)
        {
            Assert.Fail($"The embedded orkeon.d.ts is not valid TypeScript:\n{ex.Message}");
        }
    }

    private static string ReadEmbeddedRollup()
    {
        using var stream = typeof(ScriptHost).Assembly.GetManifestResourceStream(ResourceName);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
