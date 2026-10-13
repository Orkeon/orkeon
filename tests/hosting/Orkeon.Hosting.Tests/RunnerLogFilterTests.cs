using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew.Execution;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// CH-03 characterisation: the log filters a runner installs today, observed on the host the
/// runner builds (default logging without -v, ConfigureVerboseLogging with -v 1 / -v 2).
/// </summary>
public sealed class RunnerLogFilterTests : IDisposable
{
    private const string OrchestratorCategory = "Orkeon.Application.Crew.ExecutionOrchestrator";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-log-filter-" + Guid.NewGuid().ToString("N"));

    public RunnerLogFilterTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private IHost BuildHost(Action<HostBuilderContext, ILoggingBuilder>? configureLogging, params (string Key, string Value)[] extra)
    {
        var path = Path.Combine(_root, "appsettings.json");
        var settings = new Dictionary<string, string> { ["RaggableTree:Enabled"] = "false" };
        foreach (var (key, value) in extra)
            settings[key] = value;
        File.WriteAllText(path, SettingsJson.Of(settings));
        return RunnerHost.Build(path, new RunnerMountPlan(), configureLogging);
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public void ShouldKeepOrchestratorAtWarning_WhenRunnerStartsWithoutVerbosity()
    {
        using var host = BuildHost(null);
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(OrchestratorCategory);

        Assert.True(logger.IsEnabled(LogLevel.Warning));
        Assert.False(logger.IsEnabled(LogLevel.Information));
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public void ShouldShowOrchestratorAtInformation_WhenRunnerStartsAtVerbosityOne()
    {
        using var host = BuildHost((_, b) => RunnerExecution.ConfigureVerboseLogging(b, 1));
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(OrchestratorCategory);

        Assert.True(logger.IsEnabled(LogLevel.Information));
        Assert.False(logger.IsEnabled(LogLevel.Debug));
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public void ShouldShowDebug_WhenRunnerStartsAtVerbosityTwo()
    {
        using var host = BuildHost((_, b) => RunnerExecution.ConfigureVerboseLogging(b, 2));
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(OrchestratorCategory);

        Assert.True(logger.IsEnabled(LogLevel.Debug));
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public void ShouldShowVerboseCategoryAtInformation_WhenRunnerStartsWithoutVerbosity()
    {
        using var host = BuildHost(null);
        var factory = host.Services.GetRequiredService<ILoggerFactory>();

        Assert.True(factory.CreateLogger(ExecutionLogCategories.Verbose).IsEnabled(LogLevel.Information));
        Assert.False(factory.CreateLogger(OrchestratorCategory).IsEnabled(LogLevel.Information));
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public void ShouldShowVerboseCategoryAndOrchestrator_WhenRunnerStartsAtVerbosityOne()
    {
        using var host = BuildHost((_, b) => RunnerExecution.ConfigureVerboseLogging(b, 1));
        var factory = host.Services.GetRequiredService<ILoggerFactory>();

        Assert.True(factory.CreateLogger(ExecutionLogCategories.Verbose).IsEnabled(LogLevel.Information));
        Assert.True(factory.CreateLogger(OrchestratorCategory).IsEnabled(LogLevel.Information));
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public void ShouldSilenceVerboseAgents_WhenUserSetsVerboseCategoryToWarning()
    {
        using var host = BuildHost(null, ("Logging:LogLevel:" + ExecutionLogCategories.Verbose, "Warning"));
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(ExecutionLogCategories.Verbose);

        Assert.False(logger.IsEnabled(LogLevel.Information));
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public void ShouldHonourUserDefault_WhenUserSetsDefaultLevelToError()
    {
        using var host = BuildHost(null, ("Logging:LogLevel:Default", "Error"));
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(ExecutionLogCategories.Verbose);

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.False(logger.IsEnabled(LogLevel.Warning));
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public void ShouldShowVerboseAgents_WhenUserSetsDefaultLevelToInformation()
    {
        using var host = BuildHost(null, ("Logging:LogLevel:Default", "Information"));
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(ExecutionLogCategories.Verbose);

        Assert.True(logger.IsEnabled(LogLevel.Information));
    }
}
