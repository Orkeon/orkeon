using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The "Test connection" command of spec §4.2: optional, never blocking, and never a gate on
/// what can be saved.
/// </summary>
public sealed class LlmConnectionTestTests
{
    private static (LlmSectionViewModel Section, AppSettingsDocument Document) Build(
        FakeLlmEndpointProbe probe,
        string json = "{}")
    {
        var document = AppSettingsDocument.Parse(json);
        return (new LlmSectionViewModel(() => document, () => { }, probe), document);
    }

    private const string ConfiguredLlm = """
        { "Llm": { "Model": "llama3.2", "BaseUrl": "http://localhost:11434" } }
        """;

    [Fact]
    public async Task Should_ProbeTheConfiguredEndpoint_When_TheCommandRuns()
    {
        var probe = new FakeLlmEndpointProbe { Result = LlmProbeResult.Reachable(4) };
        var (section, _) = Build(probe, ConfiguredLlm);

        await section.TestConnectionCommand.ExecuteAsync();

        Assert.Equal("http://localhost:11434", Assert.Single(probe.Requests).BaseUrl);
        Assert.Equal("Endpoint reachable — 4 model(s).", section.ConnectionTestResult);
    }

    [Fact]
    public async Task Should_PresentTheInlineKey_When_TheFileCarriesOne()
    {
        var probe = new FakeLlmEndpointProbe();
        var (section, _) = Build(probe, """
            { "Llm": { "BaseUrl": "https://api.openai.com/v1", "ApiKey": "sk-from-file" } }
            """);

        await section.TestConnectionAsync(TestContext.Current.CancellationToken);

        Assert.Equal("sk-from-file", probe.LastRequest.ApiKey);
    }

    [Fact]
    public async Task Should_ReportTheReason_When_TheEndpointIsUnreachable()
    {
        var probe = new FakeLlmEndpointProbe
        {
            Result = LlmProbeResult.Unreachable("Connection refused (localhost:11434)"),
        };
        var (section, document) = Build(probe, ConfiguredLlm);

        var result = await section.TestConnectionAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains("Connection refused", section.ConnectionTestResult!, StringComparison.Ordinal);
        // The verdict is informational: the section is untouched and still saveable.
        Assert.Equal("llama3.2", document.Llm.Model);
        Assert.True(section.Exists);
    }

    [Fact]
    public async Task Should_ReportTheFailure_When_TheProbeItselfThrows()
    {
        var probe = new FakeLlmEndpointProbe { FailWith = new InvalidOperationException("probe exploded") };
        var (section, _) = Build(probe, ConfiguredLlm);

        // The command must complete: an unobserved task exception would take the app down.
        await section.TestConnectionCommand.ExecuteAsync();

        Assert.Contains("probe exploded", section.ConnectionTestResult!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_RefuseASecondRun_When_ATestIsStillInFlight()
    {
        var probe = new FakeLlmEndpointProbe { Gate = new TaskCompletionSource() };
        var (section, _) = Build(probe, ConfiguredLlm);

        var running = section.TestConnectionCommand.ExecuteAsync();

        Assert.False(running.IsCompleted);
        Assert.True(section.IsTestingConnection);
        Assert.False(section.TestConnectionCommand.CanExecute(null));
        Assert.Equal("Testing the connection…", section.ConnectionTestResult);

        probe.Gate.SetResult();
        await running;

        Assert.False(section.IsTestingConnection);
        Assert.Single(probe.Requests);
    }

    [Fact]
    public async Task Should_ProbeNothing_When_NoEndpointIsConfigured()
    {
        // An unconfigured section is still allowed to run the test; the probe is what
        // reports that there is nothing to reach.
        var probe = new FakeLlmEndpointProbe
        {
            Result = LlmProbeResult.Unreachable("no base URL is configured, so there is nothing to reach."),
        };
        var (section, _) = Build(probe);

        await section.TestConnectionAsync(TestContext.Current.CancellationToken);

        Assert.Null(probe.LastRequest.BaseUrl);
        Assert.Contains("nothing to reach", section.ConnectionTestResult!, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_StartWithNoVerdict_When_TheSectionIsCreated()
    {
        var (section, _) = Build(new FakeLlmEndpointProbe(), ConfiguredLlm);

        Assert.Null(section.ConnectionTestResult);
        Assert.False(section.IsTestingConnection);
        Assert.True(section.TestConnectionCommand.CanExecute(null));
    }

    [Fact]
    public async Task Should_ReachTheSectionThroughTheTab_When_TheTabOwnsTheProbe()
    {
        var probe = new FakeLlmEndpointProbe();
        var tab = new ConfigTabViewModel(
            new FakeAppSettingsStore(),
            new FakeDirectoryProbe(),
            new FakePathPicker(),
            processRunner: null,
            dispatcher: null,
            globalPathOverride: "/home/user/.config/Orkeon/appsettings.json",
            llmProbe: probe);

        tab.Llm.BaseUrl = OrkeonCliDefaults.OllamaDefault;
        await tab.Llm.TestConnectionCommand.ExecuteAsync();

        Assert.Equal(OrkeonCliDefaults.OllamaDefault, Assert.Single(probe.Requests).BaseUrl);
    }
}
