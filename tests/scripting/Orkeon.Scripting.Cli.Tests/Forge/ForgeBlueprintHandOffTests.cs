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

    private static ForgeBlueprint AgentPlan(string field, string text, params ForgeBlueprintTask[] tasks) =>
        new()
        {
            Crew = new ForgeBlueprintCrew { Name = "team", Goal = "goal", Process = "parallel" },
            Agents =
            [
                new ForgeBlueprintAgent
                {
                    Key = "a",
                    Role = "r",
                    Goal = field == "goal" ? text : "g",
                    Backstory = field == "backstory" ? text : null,
                },
                new ForgeBlueprintAgent { Key = "idle", Role = "r2", Goal = "g" },
            ],
            Tasks = tasks,
        };

    private static ForgeBlueprint TwoAgentPlan(string process, string goalOfA) =>
        new()
        {
            Crew = new ForgeBlueprintCrew { Name = "team", Goal = "goal", Process = process },
            Agents =
            [
                new ForgeBlueprintAgent { Key = "a", Role = "r", Goal = goalOfA },
                new ForgeBlueprintAgent { Key = "b", Role = "r2", Goal = "g" },
            ],
            Tasks =
            [
                new ForgeBlueprintTask { Key = "t1", Description = "Do", ExpectedOutput = "Done", Agent = "a", Deliverable = "/output/a.md" },
                new ForgeBlueprintTask { Key = "t2", Description = "Do", ExpectedOutput = "Done", Agent = "b", Deliverable = "/output/b.md" },
            ],
        };

    [Fact]
    [Trait("BR", "CH-47")]
    public void ShouldAcceptAPlan_WhenAGoalNamesTheFolderOfADeliverable()
    {
        var plan = AgentPlan("goal", "Collect everything in /output/reports",
            Task("w", deliverable: "/output/reports/summary.md"));

        Assert.Empty(plan.Validate([]));
    }

    [Fact]
    [Trait("BR", "CH-47")]
    public void ShouldAcceptAPlan_WhenADescriptionNamesTheFolderOfADependencyDeliverable()
    {
        var plan = Plan("parallel",
            Task("w", deliverable: "/output/reports/summary.md"),
            Task("r", "Read every file in /output/reports", dependencies: "w"));

        Assert.Empty(plan.Validate([]));
    }

    [Fact]
    [Trait("BR", "CH-47")]
    public void ShouldRefuseAPlan_WhenAnAgentNamesAPeerDeliverableOutsideItsChain()
    {
        var plan = TwoAgentPlan("sequential", "Summarise /output/b.md");

        var error = Assert.Single(plan.Validate([]));

        Assert.StartsWith("agents[0] ('a'): its goal names '/output/b.md'", error, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("BR", "CH-47")]
    [InlineData("consensual")]
    [InlineData("hierarchical")]
    [InlineData("autonomous")]
    public void ShouldAcceptAPlan_WhenAVoterNamesAnotherTaskDeliverableInAConsensualCrew(string process)
    {
        var plan = TwoAgentPlan(process, "Summarise /output/b.md");

        Assert.Empty(plan.Validate([]));
    }

    [Fact]
    [Trait("BR", "CH-47")]
    public void ShouldRefuseAPlan_WhenAnyAgentNamesAPathNoTaskDelivers()
    {
        var plan = TwoAgentPlan("consensual", "Summarise /output/ghost.md");

        var error = Assert.Single(plan.Validate([]));

        Assert.StartsWith("agents[0] ('a'): its goal names '/output/ghost.md'", error, StringComparison.Ordinal);
        Assert.Empty(plan.Validate(["/output/ghost.md"]));
    }

    [Fact]
    [Trait("BR", "CH-47")]
    public void ShouldNameNoTask_WhenATaskLessAgentIsRefused()
    {
        var plan = new ForgeBlueprint
        {
            Crew = new ForgeBlueprintCrew { Name = "team", Goal = "goal", Process = "parallel" },
            Agents =
            [
                new ForgeBlueprintAgent { Key = "a", Role = "r", Goal = "g" },
                new ForgeBlueprintAgent { Key = "idle", Role = "r2", Goal = "Maintain /output/notes.md" },
            ],
            Tasks = [Task("w", deliverable: "/output/report.md")],
        };

        var error = Assert.Single(plan.Validate([]));

        Assert.StartsWith(
            "agents[1] ('idle'): its goal names '/output/notes.md', which no task of the plan delivers and no output folder holds",
            error, StringComparison.Ordinal);
        Assert.DoesNotContain("its tasks", error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-44")]
    public void ShouldAcceptAPlan_WhenAnAgentBackstoryNamesItsOwnTaskDeliverable()
    {
        var plan = AgentPlan("backstory", "You always write /output/notes.md with care.",
            Task("w", deliverable: "/output/notes.md"));

        Assert.Empty(plan.Validate([]));
    }

    [Fact]
    [Trait("BR", "CH-44")]
    public void ShouldAcceptAPlan_WhenAnAgentGoalNamesAFileItsTaskDependsOn()
    {
        var plan = AgentPlan("goal", "Summarise /output/notes.md",
            new ForgeBlueprintTask { Key = "w", Description = "Do", ExpectedOutput = "Done", Agent = "idle", Deliverable = "/output/notes.md" },
            Task("m", dependencies: "w"),
            Task("r", dependencies: "m"));

        Assert.Empty(plan.Validate([]));
    }

    [Theory]
    [Trait("BR", "CH-44")]
    [InlineData("goal")]
    [InlineData("backstory")]
    public void ShouldRefuseAPlan_WhenAnAgentGoalNamesAFileNoTaskOfItGets(string field)
    {
        var plan = AgentPlan(field, "Turn /output/notes.md into a summary",
            Task("w", deliverable: "/output/other.md"));

        var errors = plan.Validate([]);

        var error = Assert.Single(errors, e => e.StartsWith("agents[0] ('a')", StringComparison.Ordinal));
        Assert.Contains($"its {field} names", error, StringComparison.Ordinal);
        Assert.Contains("'/output/notes.md'", error, StringComparison.Ordinal);
        Assert.Empty(plan.Validate(["/output/notes.md"]));
    }

    [Fact]
    [Trait("BR", "CH-44")]
    public void ShouldAcceptAPlan_WhenAManagerWithNoTaskNamesTheFinalDeliverable()
    {
        var plan = new ForgeBlueprint
        {
            Crew = new ForgeBlueprintCrew { Name = "team", Goal = "goal", Process = "hierarchical" },
            Manager = "lead",
            Agents =
            [
                new ForgeBlueprintAgent { Key = "a", Role = "r", Goal = "g" },
                new ForgeBlueprintAgent { Key = "lead", Role = "r2", Goal = "Deliver /output/report.md on time" },
            ],
            Tasks = [Task("w", deliverable: "/output/report.md")],
        };

        Assert.DoesNotContain(plan.Validate([]), e => e.StartsWith("agents[", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("BR", "CH-44")]
    public void ShouldRefuseAPlan_WhenAnAgentWithNoTaskNamesAFileOutsideExistingPaths()
    {
        var plan = new ForgeBlueprint
        {
            Crew = new ForgeBlueprintCrew { Name = "team", Goal = "goal", Process = "parallel" },
            Agents =
            [
                new ForgeBlueprintAgent { Key = "a", Role = "r", Goal = "g" },
                new ForgeBlueprintAgent { Key = "idle", Role = "r2", Goal = "g", Backstory = "You maintain /output/notes.md" },
            ],
            Tasks = [Task("w", deliverable: "/output/report.md")],
        };

        var error = Assert.Single(plan.Validate([]));

        Assert.StartsWith("agents[1] ('idle')", error, StringComparison.Ordinal);
        Assert.Contains("its backstory names", error, StringComparison.Ordinal);
        Assert.Contains("'/output/notes.md'", error, StringComparison.Ordinal);
        Assert.Empty(plan.Validate(["/output/notes.md"]));
    }

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
        var own = Plan("parallel", Task("w", "Write /output/notes.md", deliverable: "/output/notes.md"));
        var existing = Plan("parallel", Task("w", deliverable: "/output/x.md"), Task("r", "Read /output/pre.md"));
        var input = Plan("parallel", Task("w", deliverable: "/output/x.md"), Task("r", "Read /workspace/in.md"));

        Assert.Empty(direct.Validate([]));
        Assert.Empty(transitive.Validate([]));
        Assert.Empty(own.Validate([]));
        Assert.Empty(existing.Validate(["/output/pre.md"]));
        Assert.Empty(input.Validate([]));
    }

    [Fact]
    [Trait("BR", "CH-37")]
    public void ShouldRefuseAPlan_WhenASequentialReaderDoesNotListTheEarlierWriter()
    {
        foreach (var process in new string?[] { null, "sequential" })
        {
            var plan = Plan(process,
                Task("w", deliverable: "/output/notes.md"),
                Task("r", "Read /output/notes.md"));

            var errors = plan.Validate([]);

            var error = Assert.Single(errors);
            Assert.Contains("tasks[1] ('r')", error);
            Assert.Contains("/output/notes.md", error);
            Assert.Contains("directly or transitively, among its dependencies", error);
        }
    }

    [Fact]
    [Trait("BR", "CH-37")]
    public void ShouldAcceptAPlan_WhenAHierarchicalReaderDependsOnTheWriter()
    {
        var plan = Plan("hierarchical",
            Task("w", deliverable: "/output/notes.md"),
            Task("r", "Read /output/notes.md", dependencies: "w"));

        Assert.Empty(plan.Validate([]));
    }

    [Fact]
    [Trait("BR", "CH-34")]
    public void ShouldRefuseAPlan_WhenATaskNamesAnOutputFileNoDependencyDeclares()
    {
        var none = Plan("sequential",
            Task("w", deliverable: "/output/other.md"),
            Task("r", "Read /output/notes.md", dependencies: "w"));
        Assert.Equal([Refusal(1, "r", "/output/notes.md")], none.Validate([]));

        var descriptionOnly = Plan("sequential",
            Task("w", "Write /output/notes.md", deliverable: "/output/other.md"),
            Task("r", "Read /output/notes.md", dependencies: "w"));
        // One error per offending task: the writer names a path that is not its own deliverable either.
        Assert.Equal([Refusal(0, "w", "/output/notes.md"), Refusal(1, "r", "/output/notes.md")], descriptionOnly.Validate([]));

        var after = Plan("sequential",
            Task("r", "Read /output/notes.md"),
            Task("w", deliverable: "/output/notes.md"));
        Assert.Equal([Refusal(0, "r", "/output/notes.md")], after.Validate([]));

        var parallelBefore = Plan("parallel",
            Task("w", deliverable: "/output/notes.md"),
            Task("r", "Read /output/notes.md"));
        Assert.Equal([Refusal(1, "r", "/output/notes.md")], parallelBefore.Validate([]));
    }

    [Theory]
    [Trait("BR", "CH-34")]
    [InlineData("Read /output/a.md.", false)]
    [InlineData("Read (/output/a.md)", false)]
    [InlineData("Read /output/a.md, then stop", false)]
    [InlineData("Read `/output/a.md`", false)]
    [InlineData("Read /output/*.md", true)]
    [Trait("BR", "CH-37")]
    [InlineData("Read /output/notes.md\u202F: now", false)]
    [InlineData("Read\u00A0/output/x.md\u00A0now", false)]
    [InlineData("Read \u00ab/output/x.md\u00bb", false)]
    [InlineData("path=/output/x.md", false)]
    [InlineData("fichier:/output/x.md", false)]
    [InlineData("Read C:/output/x", true)]
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

        Assert.Equal(accepted, inDescription.Validate([]).Count == 0);
        Assert.Equal(accepted, inExpected.Validate([]).Count == 0);
    }
}
