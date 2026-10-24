using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Hosting.Tests.Doubles;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// The operator channel (<see cref="RunnerOperatorLog"/>): the warnings a run goes on after reach the
/// runner's standard error whatever level the host keeps its logger at, and a runner writes its
/// diagnostics on the <see cref="IRunnerConsole"/> it is handed, never on the process console.
/// </summary>
public sealed class RunnerOperatorLogTests : IDisposable
{
    private readonly string _root;

    private sealed class TestOptions : RunnerOptionsBase { }

    public RunnerOperatorLogTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "orkeon-operator-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    [Theory]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.None)]
    public void ShouldWriteTheOperatorWarningOnTheRunnerStderr_WhenTheHostLogsAboveWarning(LogLevel hostLevel)
    {
        // Arrange: settings with no Llm section, the host's logger kept above Warning.
        var settingsPath = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(settingsPath, "{}");
        var console = new FakeRunnerConsole();
        using var logs = new CapturingLoggerProvider();

        // Act
        using (RunnerHost.Build(
            settingsPath,
            new RunnerMountPlan(),
            configureLogging: (_, b) =>
            {
                b.ClearProviders();
                b.AddProvider(logs);
                b.SetMinimumLevel(hostLevel);
            },
            configureServices: (_, services) => services.AddSingleton<IRunnerConsole>(console)))
        {
        }

        // Assert: one line on the runner's stderr, none on its stdout, none on the host's own logger.
        var expected = "WARNING: " + RunnerHost.LlmNotConfiguredMessage;
        var lines = console.ErrorOutput.ToString().Split(Environment.NewLine);
        Assert.Single(lines, line => line == expected);
        Assert.Equal("", console.Output.ToString());
        Assert.DoesNotContain(logs.Entries, entry => entry.Category == RunnerOperatorLog.Category);
    }

    [Fact]
    public async Task ShouldWriteTheRefusalOnTheGivenConsole_WhenTheConfigIsMissing()
    {
        // Arrange
        var console = new FakeRunnerConsole();
        var options = new TestOptions { ConfigPath = "", RunnerConsole = console };

        // Act
        var exitCode = await RunnerExecution.RunOneShotAsync(
            options, "Orkeon.Hosting.Tests", externalCt: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Contains("ERROR: --config is required", console.ErrorOutput.ToString(), StringComparison.Ordinal);
        Assert.Equal("", console.Output.ToString());
    }

    [Fact]
    public async Task ShouldWriteTheMissingConfigOnTheGivenConsole_WhenTheConfigFileDoesNotExist()
    {
        // Arrange
        var console = new FakeRunnerConsole();
        var missing = Path.Combine(_root, "absent.yaml");
        var options = new TestOptions { ConfigPath = missing, RunnerConsole = console };

        // Act
        var exitCode = await RunnerExecution.RunValidateAsync(
            options, "Orkeon.Hosting.Tests", externalCt: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Contains($"ERROR: config file not found: {missing}", console.ErrorOutput.ToString(), StringComparison.Ordinal);
    }
}
