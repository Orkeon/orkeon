using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Hosting.Tests.Doubles;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// #229 - a .NET application runs a <c>crew.ork.ts</c> in its own process: one call on
/// <see cref="ICrewScriptRunner"/>, no <c>orkeon</c> process, the model a hand-written factory's.
/// </summary>
public sealed class CrewScriptRunnerTests : IDisposable
{
    private readonly string _root;
    private readonly string _outDir;

    public CrewScriptRunnerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "orkeon-crew-script-runner-" + Guid.NewGuid().ToString("N"));
        _outDir = Path.Combine(_root, "out");
        Directory.CreateDirectory(_outDir);
        File.WriteAllText(Path.Combine(_root, "crew.ork.ts"), """
            /// <reference orkeon-script="1.0" />
            const writer = agentBuilder().name("writer").role("Writer").goal("Write a note").build();
            const note = taskBuilder()
                .agent(writer)
                .description("Write a one-line note")
                .expectedOutput("A note")
                .build();
            const crew = crewBuilder().name("embedded").goal("Run inside the application")
                .withAgent(writer).withTask(note).build();
            (globalThis as any).crew = crew;
            """);
        File.WriteAllText(Path.Combine(_root, "appsettings.json"), """
            { "RaggableTree": { "Enabled": false },
              "Llm": { "Model": "embedded-model", "ApiKey": "sk-test" } }
            """);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort temp cleanup */ }
    }

    [Fact]
    public async Task ShouldRunTheScriptedCrewInProcess_WhenResolvedFromAddOrkeonScripting()
    {
        var models = new RecordingLlmProviderFactory { Answer = "Final answer: the note." };
        using var diagnostics = new StringWriter();
        var services = new ServiceCollection().AddOrkeonScripting(options =>
        {
            options.Mounts.Add($"{_outDir}:/output:rw");
            options.AllowExternalMounts = true;
            options.Diagnostics = diagnostics;
            options.ConfigureServices = (_, s) => s.AddSingleton<ILlmProviderFactory>(models);
        });
        using var provider = services.BuildServiceProvider();
        var runner = provider.GetRequiredService<ICrewScriptRunner>();

        var output = await runner.RunAsync(
            Path.Combine(_root, "crew.ork.ts"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(output.Succeeded, output.Error);
        Assert.Contains("the note", output.FinalOutput, StringComparison.Ordinal);
        var model = models.For("embedded-model");
        Assert.NotNull(model);
        Assert.True(model.ChatCalls.Count + model.GenerateCalls.Count > 0, "the crew never reached the model");
        Assert.Contains("Using settings:", diagnostics.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRefuseWithTheRunnersReason_WhenTheCrewFileIsMissing()
    {
        var runner = new CrewScriptRunner();

        var ex = await Assert.ThrowsAsync<CrewScriptRunException>(() => runner.RunAsync(
            Path.Combine(_root, "absent.ork.ts"), cancellationToken: TestContext.Current.CancellationToken));

        Assert.StartsWith("config file not found:", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldThrowOperationCanceled_WhenTheRunIsCancelledOnceTheCrewStarted()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var models = new RecordingLlmProviderFactory
        {
            Responder = () =>
            {
                // The crew reached its model: it started, then the application cancels it.
                cts.Cancel();
                return new LlmResponse { Content = "Final answer: too late." };
            },
        };
        var runner = RunnerOver(models);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            Path.Combine(_root, "crew.ork.ts"), cancellationToken: cts.Token));

        Assert.NotEmpty(models.Built);
    }

    [Fact]
    public async Task ShouldReturnAFailedOutput_WhenTheCrewFailsWithoutBeingCancelled()
    {
        var models = new RecordingLlmProviderFactory
        {
            Responder = () => throw new InvalidOperationException("the model is down"),
        };
        var runner = RunnerOver(models);

        var output = await runner.RunAsync(
            Path.Combine(_root, "crew.ork.ts"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(output.Succeeded);
        Assert.False(string.IsNullOrEmpty(output.Error));
    }

    [Fact]
    public async Task ShouldCompleteEachRunWithItsOwnOutput_WhenTwoRunsProceedAtOnce()
    {
        var second = Path.Combine(_root, "second");
        Directory.CreateDirectory(second);
        File.Copy(Path.Combine(_root, "crew.ork.ts"), Path.Combine(second, "crew.ork.ts"));
        var ct = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Path.Combine(second, "appsettings.json"), """
            { "RaggableTree": { "Enabled": false },
              "Llm": { "Model": "second-model", "ApiKey": "sk-test" } }
            """, ct);
        var models = new RecordingLlmProviderFactory { Answer = "Final answer: the note." };
        var runner = RunnerOver(models);

        var outputs = await Task.WhenAll(
            runner.RunAsync(Path.Combine(_root, "crew.ork.ts"), cancellationToken: ct),
            runner.RunAsync(Path.Combine(second, "crew.ork.ts"), cancellationToken: ct));

        Assert.All(outputs, output => Assert.True(output.Succeeded, output.Error));
        Assert.NotSame(outputs[0], outputs[1]);
        var first = models.For("embedded-model");
        var other = models.For("second-model");
        Assert.NotNull(first);
        Assert.NotNull(other);
        Assert.True(first.ChatCalls.Count + first.GenerateCalls.Count > 0, "the first run never reached its model");
        Assert.True(other.ChatCalls.Count + other.GenerateCalls.Count > 0, "the second run never reached its model");
    }

    private CrewScriptRunner RunnerOver(RecordingLlmProviderFactory models)
    {
        var options = new CrewScriptRunnerOptions
        {
            AllowExternalMounts = true,
            ConfigureServices = (_, s) => s.AddSingleton<ILlmProviderFactory>(models),
        };
        options.Mounts.Add($"{_outDir}:/output:rw");
        return new CrewScriptRunner(options);
    }
}
