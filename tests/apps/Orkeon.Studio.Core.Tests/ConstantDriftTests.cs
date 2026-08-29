using System.Reflection;
using System.Text.Json;
using Orkeon.Hosting;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Storage;
using Orkeon.Studio.Core.Targets;
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
    [Fact]
    public void Every_copied_endpoint_matches_the_runtime_constant()
    {
        Assert.Equal(LlmEndpoints.OpenAI, OrkeonCliDefaults.OpenAI);
        Assert.Equal(LlmEndpoints.Anthropic, OrkeonCliDefaults.Anthropic);
        Assert.Equal(LlmEndpoints.Groq, OrkeonCliDefaults.Groq);
        Assert.Equal(LlmEndpoints.DeepSeek, OrkeonCliDefaults.DeepSeek);
        Assert.Equal(LlmEndpoints.Together, OrkeonCliDefaults.Together);
        Assert.Equal(LlmEndpoints.Qwen, OrkeonCliDefaults.Qwen);
        Assert.Equal(LlmEndpoints.Kimi, OrkeonCliDefaults.Kimi);
        Assert.Equal(LlmEndpoints.HuggingFace, OrkeonCliDefaults.HuggingFace);
        Assert.Equal(LlmEndpoints.Mistral, OrkeonCliDefaults.Mistral);
        Assert.Equal(LlmEndpoints.Zai, OrkeonCliDefaults.Zai);
        Assert.Equal(LlmEndpoints.OllamaDefault, OrkeonCliDefaults.OllamaDefault);
    }

    [Fact]
    public void No_public_endpoint_constant_is_missing_from_the_copy()
    {
        // A provider added to LlmEndpoints without a copy here would silently be detected as
        // 'custom' by LlmProviderDetector. Vector-store endpoints are out of scope: Studio's
        // detector reads Llm:BaseUrl only.
        string[] vectorStores = [LlmEndpoints.ChromaDbDefault, LlmEndpoints.RedisDefault];

        var runtime = ConstantValuesOf(typeof(LlmEndpoints)).Except(vectorStores, StringComparer.Ordinal);
        var copied = ConstantValuesOf(typeof(OrkeonCliDefaults)).ToHashSet(StringComparer.Ordinal);

        Assert.All(runtime, endpoint =>
            Assert.True(copied.Contains(endpoint), $"LlmEndpoints value '{endpoint}' has no OrkeonCliDefaults copy."));
    }

    /// <summary>
    /// Every cloud endpoint Studio copies is one the detector recognises.
    /// <para>
    /// The neighbouring test asserts the constant was <i>copied</i>, and its own comment says
    /// a missing copy "would silently be detected as 'custom'". That is the property one step
    /// away from the one that matters: <c>OrkeonCliDefaults.Gemini</c> was copied, was pinned
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
    public void Every_copied_default_model_matches_the_runtime_lookup()
    {
        Assert.Equal(OrkeonCliDefaults.OllamaDefaultModel, ProviderDefaults.ForProvider("ollama"));
        Assert.Equal(OrkeonCliDefaults.OpenAIDefaultModel, ProviderDefaults.ForProvider("openai"));
        Assert.Equal(OrkeonCliDefaults.AnthropicDefaultModel, ProviderDefaults.ForProvider("anthropic"));
        Assert.Equal(OrkeonCliDefaults.DeepSeekDefaultModel, ProviderDefaults.ForProvider("deepseek"));
        Assert.Equal(OrkeonCliDefaults.GeminiDefaultModel, ProviderDefaults.ForProvider("gemini"));
        Assert.Equal(OrkeonCliDefaults.GroqDefaultModel, ProviderDefaults.ForProvider("groq"));
        Assert.Equal(OrkeonCliDefaults.HuggingFaceDefaultModel, ProviderDefaults.ForProvider("huggingface"));
        Assert.Equal(OrkeonCliDefaults.KimiDefaultModel, ProviderDefaults.ForProvider("kimi"));
        Assert.Equal(OrkeonCliDefaults.MistralDefaultModel, ProviderDefaults.ForProvider("mistral"));
        Assert.Equal(OrkeonCliDefaults.QwenDefaultModel, ProviderDefaults.ForProvider("qwen"));
        Assert.Equal(OrkeonCliDefaults.TogetherDefaultModel, ProviderDefaults.ForProvider("together"));
        Assert.Equal(OrkeonCliDefaults.ZaiDefaultModel, ProviderDefaults.ForProvider("zai"));
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
            [LlmPresets.Groq] = LlmEndpoints.Groq,
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
    public void The_docker_model_runner_defaults_are_the_shared_ones()
    {
        Assert.Equal(DockerModelRunnerDefaults.BaseUrl, OrkeonCliDefaults.DockerModelRunner);
        Assert.Equal(DockerModelRunnerDefaults.DefaultModel, OrkeonCliDefaults.DockerModelRunnerDefaultModel);
        Assert.Equal(DockerModelRunnerDefaults.ApiKeyPlaceholder, OrkeonCliDefaults.DockerModelRunnerApiKeyPlaceholder);

        // The preset surface the UIs bind to must resolve to the same strings.
        Assert.Equal(DockerModelRunnerDefaults.BaseUrl, LlmPresets.DockerModelRunnerBaseUrl);
        Assert.Equal(DockerModelRunnerDefaults.DefaultModel, LlmPresets.DockerModelRunnerDefaultModel);
        Assert.Equal(DockerModelRunnerDefaults.ApiKeyPlaceholder, LlmPresets.DockerModelRunnerApiKeyPlaceholder);
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

    /// <summary>
    /// Studio predicts the mounts the runner will inject so it can tell the user which
    /// configuration key each <c>--mount</c> occupies. If the virtual roots drift apart, that
    /// prediction becomes a confident lie — and, since ADR-008, the names are also the only
    /// thing standing between an agent and the operator's disk layout.
    /// </summary>
    [Fact]
    public void The_predicted_mount_roots_are_the_runner_s_own()
    {
        Assert.Equal(RunnerMounts.CrewVirtualRoot, MountAutoInjection.CrewVirtualRoot);
        Assert.Equal(RunnerMounts.ScriptVirtualRoot, MountAutoInjection.ScriptVirtualRoot);
        Assert.Equal(RunnerMounts.LlmLogVirtualRoot, MountAutoInjection.LlmLogVirtualRoot);

        // Pairwise equality cannot catch an omission: /sandbox is mounted unconditionally in
        // every host (RunnerMounts), every runner refuses a user mount claiming it, and the
        // Studio editor green-lit it because the mirror never listed it. Pin the SET.
        Assert.Equal(
            new SortedSet<string>(
                [
                    RunnerMounts.CrewVirtualRoot, RunnerMounts.ScriptVirtualRoot,
                    RunnerMounts.LlmLogVirtualRoot, RunnerMounts.SandboxVirtualRoot,
                ],
                StringComparer.Ordinal),
            new SortedSet<string>(MountAutoInjection.ReservedVirtualRoots, StringComparer.Ordinal));
    }

    /// <summary>
    /// The sub-folder a promoted team keeps its definition in. The detector descends into it
    /// so a team card can be launched; if the name drifted from what the forge renderer
    /// writes, adopted teams would stop resolving again.
    /// </summary>
    [Fact]
    public void The_promoted_crew_folder_is_the_one_the_forge_writes()
    {
        Assert.Equal(ForgeRenderReader.CrewDirectoryName, RunTargetDetector.PromotedCrewDirectoryName);
    }

    [Fact]
    public void The_win01_wording_is_the_runtime_wording()
    {
        Assert.Equal(RunnerHost.LlmNotConfiguredMessage, AppSettingsValidator.LlmNotConfiguredMessage);

        // Studio adds a note of its own, and only appends it.
        Assert.StartsWith(
            AppSettingsValidator.LlmNotConfiguredMessage,
            AppSettingsValidator.LlmNotConfiguredWarning,
            StringComparison.Ordinal);
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
