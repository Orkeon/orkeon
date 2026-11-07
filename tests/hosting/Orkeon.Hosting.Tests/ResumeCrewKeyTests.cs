using Orkeon.Hosting.Tests.Doubles;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// FWK-07: a run is resumed under the key of its crew, the crew's name and the place of its file, so
/// two crews that share a name never resume each other's state; and the refusals of
/// <c>--resume</c> are said before anything runs.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class ResumeCrewKeyTests
{
    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task ShouldNotResumeTheRunOfAnotherFile_WhenTwoCrewsShareAName()
    {
        using var harness = new ResumeRunHarness();
        var crewA = harness.WriteCrew("a", "sequential");
        var crewB = harness.WriteCrew("b", "sequential");
        var failing = new FakeAgentExecutionService();
        failing.FailOn.Add("second");
        await harness.RunAsync(crewA, failing, ct: TestContext.Current.CancellationToken);

        var serviceB = new FakeAgentExecutionService();
        var resumedB = await harness.RunAsync(crewB, serviceB, resume: "latest", ct: TestContext.Current.CancellationToken);
        var serviceA = new FakeAgentExecutionService();
        var resumedA = await harness.RunAsync(crewA, serviceA, resume: "latest", ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, resumedB.Exit);
        Assert.Empty(serviceB.Calls);
        Assert.Equal(0, resumedA.Exit);
        Assert.DoesNotContain("first", serviceA.Calls);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task ShouldNotResumeTheRunOfAnotherFile_WhenTwoFilesHaveTheSameVirtualPath()
    {
        using var harness = new ResumeRunHarness();
        // Both are /crew/crew.yaml to the virtual file system: the folder is told apart too.
        var crewA = harness.WriteCrew("one", "sequential");
        var crewB = harness.WriteCrew("two", "sequential");
        var failing = new FakeAgentExecutionService();
        failing.FailOn.Add("second");
        await harness.RunAsync(crewB, failing, ct: TestContext.Current.CancellationToken);

        var serviceA = new FakeAgentExecutionService();
        var resumedA = await harness.RunAsync(crewA, serviceA, resume: "latest", ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, resumedA.Exit);
        Assert.Empty(serviceA.Calls);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task ShouldRefuseResumeWithValidate_BeforeAnythingRuns()
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", "sequential");

        var result = await harness.RunAsync(crew, new FakeAgentExecutionService(), resume: "latest", validate: true, ct: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Exit);
        Assert.Contains("--resume", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("--validate", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task ShouldRefuseResume_WhenCheckpointingIsOff()
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", "sequential", settings: """{ "RaggableTree": { "Enabled": false } }""");
        var service = new FakeAgentExecutionService();

        var result = await harness.RunAsync(crew, service, resume: "latest", ct: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Exit);
        Assert.Empty(service.Calls);
        Assert.Contains("Orkeon:Checkpointing:Enabled", result.Stderr, StringComparison.Ordinal);
    }
}
