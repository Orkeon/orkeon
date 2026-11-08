using Orkeon.Hosting.Tests.Doubles;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// FWK-07: a task is checkpointed under its name, so two tasks of one crew with the same name would
/// share a checkpoint: the crew is refused when it is loaded, naming the task. The YAML loader already
/// refuses a key written twice (CrewYamlKeyCheck): this locks it, as the builder's own test locks the C# side.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class DuplicateTaskNameRejectedTests
{
    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task ShouldRefuseACrewThatDeclaresATaskNameTwice()
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", "sequential");
        var yaml = await File.ReadAllTextAsync(crew, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            crew,
            yaml + "\n  second:\n    description: \"Again.\"\n    expectedOutput: \"Two again.\"\n    agent: \"worker\"\n",
            TestContext.Current.CancellationToken);
        var service = new FakeAgentExecutionService();

        var result = await harness.RunAsync(crew, service, ct: TestContext.Current.CancellationToken);

        Assert.NotEqual(0, result.Exit);
        Assert.Empty(service.Calls);
        Assert.Contains("second", result.Stderr, StringComparison.Ordinal);
    }
}
