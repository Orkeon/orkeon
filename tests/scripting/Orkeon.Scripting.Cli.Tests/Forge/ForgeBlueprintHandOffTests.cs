using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>CH-34: a task may only name an output file it wrote or a task it depends on declares.</summary>
public sealed class ForgeBlueprintHandOffTests
{
    private static ForgeBlueprintTask Task(
        string key, string description = "Do the work", string? expected = "Done", string? deliverable = null, params string[] dependencies) =>
        new()
        {
            Key = key,
            Description = description,
            ExpectedOutput = expected,
            Agent = "a",
            Dependencies = dependencies,
            Deliverable = deliverable,
        };

    private static ForgeBlueprint Plan(string? process, params ForgeBlueprintTask[] tasks) =>
        new()
        {
            Crew = new ForgeBlueprintCrew { Name = "team", Goal = "goal", Process = process },
            Agents = [new ForgeBlueprintAgent { Key = "a", Role = "r", Goal = "g" }],
            Tasks = tasks,
        };

    private static string Refusal(int index, string key, string path) =>
        $"tasks[{index}] ('{key}'): names '{path}', which no task it depends on declares as its deliverable — "
        + "make it the deliverable of the task that writes it and list that task in dependencies.";

    [Fact]
    [Trait("BR", "CH-34")]
    public void ShouldAcceptAPlan_WhenTheReadFileIsADependencysDeliverable()
    {
        var direct = Plan("parallel",
            Task("w", deliverable: "/output/notes.md"),
            Task("r", "Read /output/notes.md", dependencies: "w"));
        var transitive = Plan("parallel",
            Task("w", deliverable: "/output/notes.md"),
            Task("m", dependencies: "w"),
            Task("r", "Read /output/notes.md", dependencies: "m"));
        var sequentialBlank = Plan(null,
            Task("w", deliverable: "/output/notes.md"),
            Task("r", "Read /output/notes.md"));
        var own = Plan("parallel", Task("w", "Write /output/notes.md", deliverable: "/output/notes.md"));
        var existing = Plan("parallel", Task("w", deliverable: "/output/x.md"), Task("r", "Read /output/pre.md"));
        var input = Plan("parallel", Task("w", deliverable: "/output/x.md"), Task("r", "Read /workspace/in.md"));

        Assert.Empty(direct.Validate());
        Assert.Empty(transitive.Validate());
        Assert.Empty(sequentialBlank.Validate());
        Assert.Empty(own.Validate());
        Assert.Empty(existing.Validate(["/output/pre.md"]));
        Assert.Empty(input.Validate());
    }

    [Fact]
    [Trait("BR", "CH-34")]
    public void ShouldRefuseAPlan_WhenATaskNamesAnOutputFileNoDependencyDeclares()
    {
        var none = Plan("sequential",
            Task("w", deliverable: "/output/other.md"),
            Task("r", "Read /output/notes.md", dependencies: "w"));
        Assert.Equal([Refusal(1, "r", "/output/notes.md")], none.Validate());

        var descriptionOnly = Plan("sequential",
            Task("w", "Write /output/notes.md", deliverable: "/output/other.md"),
            Task("r", "Read /output/notes.md", dependencies: "w"));
        // One error per offending task: the writer names a path that is not its own deliverable either.
        Assert.Equal([Refusal(0, "w", "/output/notes.md"), Refusal(1, "r", "/output/notes.md")], descriptionOnly.Validate());

        var after = Plan("sequential",
            Task("r", "Read /output/notes.md"),
            Task("w", deliverable: "/output/notes.md"));
        Assert.Equal([Refusal(0, "r", "/output/notes.md")], after.Validate());

        var parallelBefore = Plan("parallel",
            Task("w", deliverable: "/output/notes.md"),
            Task("r", "Read /output/notes.md"));
        Assert.Equal([Refusal(1, "r", "/output/notes.md")], parallelBefore.Validate());
    }

    [Theory]
    [Trait("BR", "CH-34")]
    [InlineData("Read /output/a.md.", false)]
    [InlineData("Read (/output/a.md)", false)]
    [InlineData("Read /output/a.md, then stop", false)]
    [InlineData("Read `/output/a.md`", false)]
    [InlineData("Read /output/*.md", true)]
    [InlineData("Read every file in /output/sub/", true)]
    [InlineData("Read /output/résumé-été.md", false)]
    [InlineData("Read /output/a.md", false)]
    [InlineData("Read /workspace/a.md and /outputs/a.md", true)]
    [InlineData("Read /sources/notes/a.md", true)]
    [InlineData("see https://x.org/notes/a", true)]
    [InlineData("Read (/notes/a.md)", false)]
    [InlineData("Read `/notes/a.md`", false)]
    public void ShouldExtractOnlyConcretePaths_FromDescriptionAndExpectedOutput(string text, bool accepted)
    {
        var inDescription = Plan("parallel", Task("w", deliverable: "/output/w.md"), Task("n", deliverable: "/notes/s.md"), Task("r", text));
        var inExpected = Plan("parallel", Task("w", deliverable: "/output/w.md"), Task("n", deliverable: "/notes/s.md"), Task("r", expected: text));

        Assert.Equal(accepted, inDescription.Validate().Count == 0);
        Assert.Equal(accepted, inExpected.Validate().Count == 0);
    }
}
