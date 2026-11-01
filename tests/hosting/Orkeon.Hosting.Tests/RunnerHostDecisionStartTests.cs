using Microsoft.Extensions.Logging;
using Orkeon.Hosting.Tests.Doubles;

namespace Orkeon.Hosting.Tests;

/// <summary>The start of a shipped host says which decision provider it runs, and what it ignored.</summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerHostDecisionStartTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-decision-start-" + Guid.NewGuid().ToString("N"));

    public RunnerHostDecisionStartTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private IReadOnlyList<CapturedLogEntry> Start(string json)
    {
        var path = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(path, json);
        using var logs = new CapturingLoggerProvider();
        var original = Console.Error;
        using var muted = new StringWriter();
        Console.SetError(muted);
        try
        {
            using var host = RunnerHost.Build(
                path,
                new RunnerMountPlan { CliMounts = [$"{_root}:/workspace:ro"], AllowExternalMounts = true },
                configureLogging: (_, b) =>
                {
                    b.ClearProviders();
                    b.AddProvider(logs);
                    b.SetMinimumLevel(LogLevel.Information);
                });
            return logs.Entries;
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    [Trait("BR", "BR-06")]
    [Trait("BR", "UC-02")]
    public void ShouldLogProviderModelAddressAndHow_WhenTheHostStarts()
    {
        var entries = Start("""{ "RaggableTree": { "Enabled": false }, "Orkeon": { "Decision": { "Provider": "kev" }, "Kev": { "Model": "kev-9b" } } }""");

        Assert.Single(entries, e => e.Message.Contains("kev-9b", StringComparison.Ordinal)
            && e.Message.Contains("http://127.0.0.1:8009/v1", StringComparison.Ordinal)
            && e.Message.Contains("named", StringComparison.Ordinal)
            && e.Message.Contains("kev", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("BR", "BR-15")]
    [Trait("BR", "UC-01")]
    public void ShouldWarnPerIgnoredSection_WhenTheHostStarts()
    {
        var entries = Start("""{ "RaggableTree": { "Enabled": false }, "Orkeon": { "Decision": { "Provider": "liquid" }, "TypeSafe": { "Model": "m" } } }""");

        Assert.Single(entries, e => e.Level == LogLevel.Warning && e.Message.Contains("Orkeon:TypeSafe", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("BR", "BR-05")]
    [Trait("BR", "BR-15")]
    public void ShouldWarnNamingTheProviderSetting_WhenOnlyAKevSectionIsPresent()
    {
        var entries = Start("""{ "RaggableTree": { "Enabled": false }, "Orkeon": { "Kev": { "Model": "kev-9b" } } }""");

        Assert.Single(entries, e => e.Level == LogLevel.Warning
            && e.Message.Contains("Orkeon:Kev", StringComparison.Ordinal)
            && e.Message.Contains("Orkeon:Decision:Provider", StringComparison.Ordinal));
        Assert.DoesNotContain(entries, e => e.Level == LogLevel.Information
            && (e.Message.Contains("named", StringComparison.Ordinal) || e.Message.Contains("deduced", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("BR", "BR-06")]
    public void ShouldLogHowDeduced_WhenTheProviderIsDeducedFromItsSection()
    {
        var entries = Start("""{ "RaggableTree": { "Enabled": false }, "Orkeon": { "Liquid": { "Model": "lq" } } }""");

        Assert.Single(entries, e => e.Message.Contains("liquid", StringComparison.OrdinalIgnoreCase)
            && e.Message.Contains("deduced", StringComparison.Ordinal));
    }
}
