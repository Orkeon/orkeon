using Orkeon.Studio.Config.Presentation;
using Orkeon.Studio.Config.Tests.Doubles;
using Orkeon.Studio.Config.Views;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Presets;

namespace Orkeon.Studio.Config.Tests.Views;

/// <summary>
/// The "Test connection" button of SPEC §4.2: optional, never blocking. The screen is built
/// headlessly, so the assertions read the view's own state rather than the rendered label —
/// <c>Application.Invoke</c> does not pump outside <c>Application.Init</c>.
/// </summary>
public sealed class LlmConnectionTestTests
{
    [Fact]
    public async Task The_button_probes_the_endpoint_currently_on_screen()
    {
        var form = new LlmForm { BaseUrl = OrkeonCliDefaults.OllamaDefault };
        var probe = new FakeLlmEndpointProbe { Result = LlmProbeResult.Reachable(3) };
        using var view = new LlmSectionView(form, probe);
        view.Load();

        await view.TestConnection();

        Assert.Equal(OrkeonCliDefaults.OllamaDefault, Assert.Single(probe.Requests).BaseUrl);
        Assert.Equal("Endpoint reachable — 3 model(s).", view.TestResult);
    }

    [Fact]
    public async Task An_endpoint_typed_but_not_yet_committed_is_the_one_probed()
    {
        // The user types a new URL and hits the button without leaving the screen.
        var form = new LlmForm { BaseUrl = OrkeonCliDefaults.OllamaDefault };
        var probe = new FakeLlmEndpointProbe();
        using var view = new LlmSectionView(form, probe);
        view.Load();

        form.BaseUrl = "http://elsewhere:9999";
        view.Load();
        await view.TestConnection();

        Assert.Equal("http://elsewhere:9999", probe.LastRequest.BaseUrl);
    }

    [Fact]
    public async Task The_screen_stays_alive_while_the_endpoint_is_being_reached()
    {
        var probe = new FakeLlmEndpointProbe { Gate = new TaskCompletionSource() };
        using var view = new LlmSectionView(new LlmForm { BaseUrl = OrkeonCliDefaults.OpenAI }, probe);

        var running = view.TestConnection();

        // The click returned while the probe is still in flight — that is what "never
        // blocking" means here, and the screen says so rather than looking frozen.
        Assert.False(running.IsCompleted);
        Assert.Equal("Testing the connection…", view.TestResult);

        probe.Gate.SetResult();
        await running;

        Assert.Contains("reachable", view.TestResult, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_probe_is_a_message_and_nothing_more()
    {
        var probe = new FakeLlmEndpointProbe
        {
            Result = LlmProbeResult.Unreachable("Connection refused (localhost:11434)"),
        };
        var form = new LlmForm { BaseUrl = OrkeonCliDefaults.OllamaDefault, Model = "llama3.2" };
        using var view = new LlmSectionView(form, probe);

        await view.TestConnection();

        Assert.Contains("Connection refused", view.TestResult, StringComparison.Ordinal);
        // The verdict changes nothing about the document: the section still saves.
        var document = Orkeon.Studio.Core.Configuration.AppSettingsDocument.CreateEmpty();
        Assert.Empty(form.ApplyTo(document));
        Assert.Equal("llama3.2", document.Llm.Model);
    }

    [Fact]
    public async Task An_unexpected_failure_lands_in_the_label_instead_of_faulting_the_task()
    {
        var probe = new FakeLlmEndpointProbe { FailWith = new InvalidOperationException("probe exploded") };
        using var view = new LlmSectionView(new LlmForm { BaseUrl = OrkeonCliDefaults.OpenAI }, probe);

        await view.TestConnection();

        Assert.Contains("probe exploded", view.TestResult, StringComparison.Ordinal);
    }

    [Fact]
    public void The_probed_key_falls_back_to_the_environment_variable()
    {
        // Studio tells users to keep the key in ORKEON_Llm__ApiKey, so the probe has to
        // look there — otherwise the button fails for everyone who followed the advice.
        var form = new LlmForm { BaseUrl = OrkeonCliDefaults.OpenAI };

        var request = form.ToProbeRequest(name =>
            string.Equals(name, LlmPresets.DefaultApiKeyEnv, StringComparison.Ordinal) ? "sk-from-env" : null);

        Assert.Equal("sk-from-env", request.ApiKey);
    }

    [Fact]
    public void An_inline_key_wins_over_the_environment_variable()
    {
        var form = new LlmForm { BaseUrl = OrkeonCliDefaults.OpenAI, ApiKey = "sk-from-file" };

        var request = form.ToProbeRequest(_ => "sk-from-env");

        Assert.Equal("sk-from-file", request.ApiKey);
    }
}
