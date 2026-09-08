using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests.Commands;

/// <summary>
/// The shape detector picks which of the two engines runs a <c>.ork.ts</c>, and it had no
/// test at all. The bug these pin was found by writing the documentation: a script whose
/// COMMENT explains `globalThis.crew = crew` was routed as declarative and then failed with
/// "did not assign globalThis.crew" -- and the files most likely to mention the line in
/// prose are exactly the ones that teach the DSL.
/// </summary>
public class CrewHandoffDetectorTests
{
    [Theory]
    [InlineData("globalThis.crew = crew;")]
    [InlineData("(globalThis as any).crew = crew;")]
    [InlineData("globalThis . crew  =  crew ;")]
    [InlineData("const crew = crewBuilder().build();\nglobalThis.crew = crew;\n")]
    public void Detects_a_real_handoff(string source)
        => Assert.True(CrewHandoffDetector.DeclaresHandoff(source));

    [Theory]
    // The regression: prose that names the line.
    [InlineData("// the shape that ends with `globalThis.crew = crew`\nawait crew.run();")]
    [InlineData("/* globalThis.crew = crew is the declarative handoff */\nawait crew.run();")]
    [InlineData("/**\n * globalThis.crew = crew\n */\nawait crew.run();")]
    [InlineData("const help = \"write globalThis.crew = crew instead\";\nawait crew.run();")]
    [InlineData("const help = `globalThis.crew = crew`;\nawait crew.run();")]
    [InlineData("await crew.run();")]
    [InlineData("")]
    public void Ignores_a_mention_that_is_not_an_assignment(string source)
        => Assert.False(CrewHandoffDetector.DeclaresHandoff(source));

    [Fact]
    public void A_url_in_a_string_does_not_start_a_comment()
    {
        // If `//` inside a string opened a line comment, everything after it would be
        // blanked and a handoff on the SAME line would be missed.
        const string source = "const doc = \"https://orkeon.dev/x\"; globalThis.crew = crew;";

        Assert.True(CrewHandoffDetector.DeclaresHandoff(source));
    }

    [Fact]
    public void A_quote_inside_a_comment_does_not_open_a_string()
    {
        const string source = "// don't use the procedural shape here\nglobalThis.crew = crew;";

        Assert.True(CrewHandoffDetector.DeclaresHandoff(source));
    }

    [Fact]
    public void Stripping_preserves_line_geometry()
    {
        // The pattern is line-anchored (`[^\r\n]{0,60}`), so a stripper that dropped or
        // added newlines would silently change which text counts as "one line".
        const string source = "// a\n/* b\n c */\n`d\ne`\n";

        var stripped = CrewHandoffDetector.StripCommentsAndStrings(source);

        Assert.Equal(source.Count(c => c == '\n'), stripped.Count(c => c == '\n'));
        Assert.Equal(source.Length, stripped.Length);
    }

    [Fact]
    public void The_shipped_procedural_examples_are_not_read_as_declarative()
    {
        // 10-inputs-and-memory.ork.ts is the file that exposed this: it documents both
        // shapes in a comment and uses the procedural one.
        var repo = RepoRoot();
        var script = Path.Combine(repo, "examples", "scripting", "10-inputs-and-memory.ork.ts");
        Assert.True(File.Exists(script), $"Missing example '{script}'.");

        Assert.False(CrewHandoffDetector.DeclaresHandoff(File.ReadAllText(script)));
    }

    [Fact]
    public void The_shipped_declarative_example_is_read_as_declarative()
    {
        var repo = RepoRoot();
        var script = Path.Combine(repo, "examples", "scripting", "crew-review-desk", "main.ork.ts");
        Assert.True(File.Exists(script), $"Missing example '{script}'.");

        Assert.True(CrewHandoffDetector.DeclaresHandoff(File.ReadAllText(script)));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Orkeon.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
