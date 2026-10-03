using Orkeon.Constants.Llm;
using Orkeon.Studio.Config.Presentation;
using Orkeon.Studio.Config.Tests.Doubles;
using Orkeon.Studio.Config.Views;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Presets;

namespace Orkeon.Studio.Config.Tests.Views;

/// <summary>
/// The "Test connection" button of SPEC §4.2: optional, never blocking. The screen is built
/// headlessly, so the assertions read the view's own state rather than the rendered label —
/// <c>Application.Invoke</c> does not pump outside <c>Application.Init</c>. The screen reads the
/// environment a test stages (an empty one by default), never the machine's.
/// </summary>
public sealed class LlmConnectionTestTests
{
    private static LlmSectionView View(LlmForm form, FakeLlmEndpointProbe probe, FakeEnvironmentVariables? environment = null) =>
        new(form, probe, environment ?? new FakeEnvironmentVariables());

    [Fact]
    public async Task The_button_probes_the_endpoint_currently_on_screen()
    {
        var form = new LlmForm { BaseUrl = LlmProviderEndpoints.OllamaDefault };
        var probe = new FakeLlmEndpointProbe { Result = LlmProbeResult.Reachable(3) };
        using var view = View(form, probe);
        view.Load();

        await view.TestConnection();

        Assert.Equal(LlmProviderEndpoints.OllamaDefault, Assert.Single(probe.Requests).BaseUrl);
        Assert.Equal("Endpoint reachable — 3 model(s).", view.TestResult);
    }

    [Fact]
    public async Task An_endpoint_typed_but_not_yet_committed_is_the_one_probed()
    {
        // The user types a new URL and hits the button without leaving the screen — an endpoint
        // that needs a key, which the form holds (STUDIO-54).
        var form = new LlmForm { BaseUrl = LlmProviderEndpoints.OllamaDefault, ApiKey = "sk-test" };
        var probe = new FakeLlmEndpointProbe();
        using var view = View(form, probe);
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
        // A key in the form: OpenAI refuses every call without one, and so does the screen (STUDIO-54).
        using var view = View(new LlmForm { BaseUrl = LlmProviderEndpoints.OpenAI, ApiKey = "sk-test" }, probe);

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
        var form = new LlmForm { BaseUrl = LlmProviderEndpoints.OllamaDefault, Model = "llama3.2" };
        using var view = View(form, probe);

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
        using var view = View(new LlmForm { BaseUrl = LlmProviderEndpoints.OpenAI, ApiKey = "sk-test" }, probe);

        await view.TestConnection();

        Assert.Contains("probe exploded", view.TestResult, StringComparison.Ordinal);
    }

    [Fact]
    public void The_probed_key_falls_back_to_the_environment_variable()
    {
        // Studio tells users to keep the key in ORKEON_Llm__ApiKey, so the probe has to
        // look there — otherwise the button fails for everyone who followed the advice.
        var form = new LlmForm { BaseUrl = LlmProviderEndpoints.OpenAI };

        var request = form.ToProbeRequest(name =>
            string.Equals(name, LlmPresets.DefaultApiKeyEnv, StringComparison.Ordinal) ? "sk-from-env" : null);

        Assert.Equal("sk-from-env", request.ApiKey);
    }

    [Fact]
    public void The_probed_key_follows_the_reference_as_a_run_does()
    {
        // STUDIO-49: the variable Llm:ApiKeyEnvVar names is where a run finds the key.
        var form = new LlmForm { BaseUrl = LlmProviderEndpoints.Zai, ApiKeyEnvVar = "ZAI_API_KEY" };

        var request = form.ToProbeRequest(name => name == "ZAI_API_KEY" ? "sk-zai" : null);

        Assert.Equal("sk-zai", request.ApiKey);
    }

    /// <summary>
    /// STUDIO-54, decision 2: a run composes the file, then the <c>ORKEON_</c> variables over it —
    /// the screen's own help says « with precedence over this file ». The test presented the file's
    /// key first: with both, it judged a key the run never sends.
    /// </summary>
    [Fact]
    public void The_orkeon_variable_wins_over_the_files_key_as_in_a_run()
    {
        var form = new LlmForm { BaseUrl = LlmProviderEndpoints.OpenAI, ApiKey = "sk-from-file" };

        var request = form.ToProbeRequest(name =>
            string.Equals(name, LlmPresets.DefaultApiKeyEnv, StringComparison.Ordinal) ? "sk-from-env" : null);

        Assert.Equal("sk-from-env", request.ApiKey);
    }

    [Fact]
    public async Task The_screen_presents_the_key_the_run_presents()
    {
        var environment = new FakeEnvironmentVariables();
        environment.Process[LlmPresets.DefaultApiKeyEnv] = "sk-from-env";
        var probe = new FakeLlmEndpointProbe();
        using var view = View(new LlmForm { BaseUrl = LlmProviderEndpoints.DeepSeek, ApiKey = "sk-faux" }, probe, environment);

        await view.TestConnection();

        Assert.Equal("sk-from-env", probe.LastRequest.ApiKey);
    }

    /// <summary>
    /// STUDIO-54, decision 2: without any key, a run refuses every endpoint but Ollama's (« API key is
    /// required »), and so does the screen — at once, without a request, with the remedy the TUI has:
    /// it remembers no key.
    /// </summary>
    [Fact]
    public async Task An_endpoint_that_needs_a_key_is_refused_without_a_request_when_none_resolves()
    {
        var probe = new FakeLlmEndpointProbe();
        using var view = View(new LlmForm { BaseUrl = LlmProviderEndpoints.DeepSeek, Model = "deepseek-v4-flash" }, probe);

        await view.TestConnection();

        Assert.Empty(probe.Requests);
        Assert.StartsWith("API key missing", view.TestResult, StringComparison.Ordinal);
        Assert.Contains("API key variable", view.TestResult, StringComparison.Ordinal);
        Assert.Contains(LlmPresets.DefaultApiKeyEnv, view.TestResult, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_ollama_endpoint_is_probed_without_a_key()
    {
        var probe = new FakeLlmEndpointProbe();
        using var view = View(new LlmForm { BaseUrl = LlmProviderEndpoints.OllamaDefault, Model = "llama3.2" }, probe);

        await view.TestConnection();

        Assert.Null(Assert.Single(probe.Requests).ApiKey);
    }

    [Fact]
    public async Task A_docker_model_runner_endpoint_is_probed_with_the_placeholder_its_preset_writes()
    {
        var probe = new FakeLlmEndpointProbe();
        using var view = View(new LlmForm
        {
            BaseUrl = LlmProviderEndpoints.DockerModelRunner,
            Model = LlmProviderDefaultModels.DockerModelRunner,
            ApiKey = LlmProviderDefaultModels.DockerModelRunnerApiKeyPlaceholder,
        }, probe);

        await view.TestConnection();

        Assert.Equal("not-needed", Assert.Single(probe.Requests).ApiKey);
    }

    [Fact]
    public void The_probe_exercises_the_typed_model_and_thinking_switch()
    {
        // STUDIO-43: the same two steps as Studio's profile editor.
        var form = new LlmForm
        {
            BaseUrl = LlmProviderEndpoints.Zai,
            Model = " glm-5.2 ",
            ThinkingEnabled = "false",
            TimeoutSeconds = "600",
        };

        var request = form.ToProbeRequest(_ => "sk-zai");

        Assert.Equal("glm-5.2", request.Model);
        Assert.False(request.ThinkingEnabled);
        Assert.True(request.CheckCompletion);
        Assert.Equal(TimeSpan.FromSeconds(30), request.Timeout);
    }
}
