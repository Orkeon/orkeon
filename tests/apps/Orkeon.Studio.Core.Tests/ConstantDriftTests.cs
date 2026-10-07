using Orkeon.Constants.Llm;
using Orkeon.Constants.FileSystem;
using System.Reflection;
using System.Text.Json;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Constants.Protocol;
using Orkeon.Hosting;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Email;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Run;
using Orkeon.Studio.Core.Storage;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Core.Tests.Doubles;
using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// Studio Core deliberately copies a handful of runtime constants and ~40 lines of settings
/// path logic instead of referencing <c>Orkeon.Infrastructure</c> and <c>Orkeon.Hosting</c>:
/// those two references dragged ONNX, tree-sitter and the local embedding model into every
/// self-contained Studio publish (STUDIO-01 §7). This test project keeps both references so
/// the copies can be checked against the originals — that check is the whole price of the
/// duplication, so it must stay exhaustive.
/// </summary>
public sealed class ConstantDriftTests
{
    /// <summary>
    /// Every cloud endpoint Studio copies is one the detector recognises.
    /// <para>
    /// The neighbouring test asserts the constant was <i>copied</i>, and its own comment says
    /// a missing copy "would silently be detected as 'custom'". That is the property one step
    /// away from the one that matters: <c>LlmProviderEndpoints.Gemini</c> was copied, was pinned
    /// by that test, and <c>LlmProviderDetector</c> never registered it — so Studio reported
    /// "custom" for the endpoint its own preset catalogue writes, with a green drift suite.
    /// Ask the detector.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_copied_cloud_endpoint_is_recognised_by_the_detector()
    {
        // Local endpoints are told apart by port and path, not by host, and are covered by
        // LlmProviderDetectorTests; vector stores are not LLM endpoints at all.
        string[] notCloudLlmEndpoints =
        [
            LlmEndpoints.ChromaDbDefault,
            LlmEndpoints.PineconeControlPlane,
            LlmEndpoints.RedisDefault,
            LlmEndpoints.OllamaDefault,
        ];

        var cloudEndpoints = ConstantValuesOf(typeof(LlmEndpoints))
            .Except(notCloudLlmEndpoints, StringComparer.Ordinal)
            .Where(value => Uri.TryCreate(value, UriKind.Absolute, out var uri) && !uri.IsLoopback)
            .ToList();

        Assert.NotEmpty(cloudEndpoints);
        Assert.All(cloudEndpoints, endpoint =>
            Assert.True(
                LlmProviderDetector.Detect(endpoint) != LlmProviderDetector.Custom,
                $"LlmProviderDetector reports 'custom' for '{endpoint}' — an endpoint Orkeon itself writes."));
    }

    [Fact]
    public void The_provider_catalogue_serves_the_runtime_defaults_verbatim()
    {
        var catalogue = LlmPresets.ProviderCatalogFor(Orkeon.Studio.Core.Localization.EnglishStudioStrings.Instance);

        foreach (var (id, endpoint) in new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [LlmPresets.Anthropic] = LlmEndpoints.Anthropic,
            [LlmPresets.DeepSeek] = LlmEndpoints.DeepSeek,
            [LlmPresets.Gemini] = LlmEndpoints.Gemini,
            [LlmPresets.Grok] = LlmEndpoints.Grok,
            [LlmPresets.MiniMax] = LlmEndpoints.MiniMax,
            [LlmPresets.OpenRouter] = LlmEndpoints.OpenRouter,
            [LlmPresets.Mammouth] = LlmEndpoints.Mammouth,
            [LlmPresets.HuggingFace] = LlmEndpoints.HuggingFace,
            [LlmPresets.Kimi] = LlmEndpoints.Kimi,
            [LlmPresets.Mistral] = LlmEndpoints.Mistral,
            [LlmPresets.Qwen] = LlmEndpoints.Qwen,
            [LlmPresets.Together] = LlmEndpoints.Together,
            [LlmPresets.Zai] = LlmEndpoints.Zai,
        })
        {
            var card = Assert.Single(catalogue, c => string.Equals(c.Name, id, StringComparison.Ordinal));
            Assert.Equal(endpoint, card.DefaultBaseUrl);
            Assert.Equal(ProviderDefaults.ForProvider(id), card.DefaultModel);
        }
    }

    [Fact]
    public void The_docker_model_runner_defaults_are_what_the_committed_template_ships()
    {
        // "parity with examples/appsettings/appsettings.json" is claimed by three doc comments;
        // this is the only thing that makes it true.
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepositoryRoot(), "examples", "appsettings", "appsettings.json")));
        var llm = document.RootElement.GetProperty("Llm");

        Assert.Equal(DockerModelRunnerDefaults.BaseUrl, llm.GetProperty("BaseUrl").GetString());
        Assert.Equal(DockerModelRunnerDefaults.DefaultModel, llm.GetProperty("Model").GetString());
        Assert.Equal(DockerModelRunnerDefaults.ApiKeyPlaceholder, llm.GetProperty("ApiKey").GetString());
    }

    [Fact]
    public void The_global_settings_path_is_the_one_the_cli_writes()
    {
        // Both read the same environment; if the copied resolution ever diverges, Studio would
        // edit a file `orkeon run` never loads.
        Assert.Equal(RunnerSettings.GetGlobalSettingsPath(), SettingsLocations.GetGlobalSettingsPath());
    }

    [Fact]
    public void The_declared_minimum_cli_version_is_one_this_repository_has_reached()
    {
        // The notice tells users directory crews need >= MinimumCliVersion and that the
        // co-installed CLI has it. That is only honest while the repository's own version is
        // at least the declared minimum.
        var declared = NumericVersion(RunTargetRequirements.MinimumCliVersion);
        var built = NumericVersion(BuiltVersion());

        Assert.True(
            declared <= built,
            $"MinimumCliVersion {RunTargetRequirements.MinimumCliVersion} is ahead of the built version {built}.");
    }

    /// <summary>
    /// The status bar names the model the agents work on, and a reading from the machinery around
    /// them — a judge, a RAG pipeline, the manager — leaves that name alone (STUDIO-30). Studio
    /// sorts the kinds of work the engine names; a kind added there and not sorted here would read
    /// as an agent's work, so every one of them is checked. <c>unattributed</c> never reaches the
    /// wire: the CLI leaves the field out.
    /// </summary>
    [Fact]
    public void Every_kind_of_work_the_engine_names_is_sorted_by_the_run_meter()
    {
        var engine = ConstantValuesOf(typeof(LlmUsageOperations))
            .Where(kind => !string.Equals(kind, LlmUsageOperations.Unattributed, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(engine, ConstantValuesOf(typeof(RunCostOperations)).Order(StringComparer.Ordinal));
        Assert.All(engine, kind => Assert.Equal(
            string.Equals(kind, LlmUsageOperations.Agent, StringComparison.Ordinal),
            RunCostOperations.IsAgentWork(kind)));
    }

    /// <summary>
    /// The sign-in of an e-mail account is a stream the CLI writes and Studio reads (STUDIO-70),
    /// and a kind Studio does not read is not an error anywhere: the panel would simply never say
    /// what to do. Every kind the shared vocabulary declares must therefore come out of the client
    /// as something — a step, the completion, or the refusal — and never be skipped. The other
    /// half, that the CLI writes these kinds and no other, is pinned by <c>EmailCommandTests</c>.
    /// </summary>
    [Fact]
    public async Task Every_kind_of_the_email_sign_in_is_read_by_studio()
    {
        var read = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kind in EmailEventKinds.All)
        {
            var launcher = new FakeProcessLauncher().WithStandardOutput(
                $$"""{"v":2,"seq":1,"kind":"{{kind}}","verification_uri":"https://example.com/device","user_code":"CODE","expires_in":60,"authorization_uri":"https://example.com/auth","account":"a","code":"LoginRequired","message":"refused"}""");
            var probe = new FakeExecutableProbe { BaseDirectory = "/opt/orkeon" }.WithFile("/opt/orkeon/orkeon");
            var client = new EmailCliClient(new OrkeonProcessRunner(launcher, new OrkeonBinaryLocator(probe, ["orkeon"])));
            var steps = new List<EmailLoginStep>();

            var ended = await client.LoginAsync("a", "/tmp/appsettings.json", steps.Add, cancellationToken: TestContext.Current.CancellationToken);

            read[kind] = steps.Count > 0
                ? steps[0].Kind.ToString()
                : ended.Failure is { } failure ? failure.Kind.ToString() : ended.Kind.ToString();
        }

        // Four kinds, four distinct readings — and none of them "the verb said nothing I know".
        Assert.Equal(EmailEventKinds.All.Count, read.Values.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(nameof(EmailCliFailureKind.Unreadable), read.Values);
        Assert.Equal(nameof(EmailLoginStepKind.DeviceCode), read[EmailEventKinds.LoginDeviceCode]);
        Assert.Equal(nameof(EmailLoginStepKind.AuthorizationUrl), read[EmailEventKinds.LoginAuthorizationUrl]);
        Assert.Equal(nameof(EmailLoginStepKind.Completed), read[EmailEventKinds.LoginCompleted]);
        Assert.Equal(nameof(EmailCliFailureKind.Refused), read[EmailEventKinds.Error]);
    }

    private static string BuiltVersion() =>
        typeof(RunTargetRequirements).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(RunTargetRequirements).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    /// <summary>The <c>major.minor.patch</c> head of a version string, ignoring any suffix.</summary>
    private static Version NumericVersion(string version)
    {
        var head = new string([.. version.TakeWhile(c => char.IsDigit(c) || c == '.')]).TrimEnd('.');
        return Version.TryParse(head, out var parsed) ? parsed : new Version(0, 0, 0);
    }

    private static IEnumerable<string> ConstantValuesOf(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Orkeon.sln")))
            directory = directory.Parent;

        Assert.True(directory is not null, $"Could not locate the repo root above {AppContext.BaseDirectory}.");
        return directory!.FullName;
    }
}
