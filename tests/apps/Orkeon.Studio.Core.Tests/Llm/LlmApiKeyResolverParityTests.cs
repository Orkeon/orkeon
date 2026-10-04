using System.Text.Json;
using Orkeon.Hosting;
using Orkeon.Infrastructure.LLMs.Profiles;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Presets;

namespace Orkeon.Studio.Core.Tests.Llm;

/// <summary>
/// Serialises the tests that set a variable the runtime reads by its well-known name —
/// <c>ORKEON_Llm__ApiKey</c>, <c>Llm__ApiKey</c> — in this process's own environment, which every
/// test of the assembly that builds a configuration would otherwise see.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection
{
    public const string Name = "studio-core-process-environment";
}

/// <summary>
/// STUDIO-54, decision 2: the key the <c>orkeon-studio-config</c> probe presents is the key a run
/// presents. The runtime's own reader is the reference — <see cref="RunnerSettings.ReadConfiguration"/>
/// composes the sources as every runner does, <see cref="LlmSettings.ReadDefault"/> reads the key —,
/// over the same settings file and the same process environment as <see cref="LlmApiKeyResolver"/>.
/// The variables are real: the collection is serial, and every one is put back in <c>finally</c>.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class LlmApiKeyResolverParityTests : IDisposable
{
    private const string UnprefixedApiKey = "Llm__ApiKey";
    private const string OrkeonReference = "ORKEON_Llm__ApiKeyEnvVar";
    private const string UnprefixedReference = "Llm__ApiKeyEnvVar";

    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "orkeon-key-parity-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    /// <summary>
    /// What each layer holds — <c>ORKEON_Llm__ApiKey</c> in the process, the settings file's
    /// <c>Llm:ApiKey</c>, <c>Llm__ApiKey</c> in the process, the variable the file's
    /// <c>Llm:ApiKeyEnvVar</c> names; null when unset — and the key a run sends.
    /// </summary>
    [Theory]
    [InlineData("sk-orkeon", "sk-file", "sk-unprefixed", "sk-referenced", "sk-orkeon")]
    [InlineData(null, "sk-file", "sk-unprefixed", "sk-referenced", "sk-file")]
    [InlineData(null, null, "sk-unprefixed", "sk-referenced", "sk-unprefixed")]
    [InlineData(null, null, null, "sk-referenced", "sk-referenced")]
    [InlineData("sk-orkeon", null, null, "sk-referenced", "sk-orkeon")]
    [InlineData(null, null, null, null, null)]
    public async Task The_tui_presents_the_key_the_runtime_reads(
        string? orkeon, string? file, string? unprefixed, string? referenced, string? expected)
    {
        var reference = "ORKEON_PARITY_" + Guid.NewGuid().ToString("N");
        var settings = await WriteSettingsAsync(file, reference);

        using (ProcessVariables.Set(
                   (LlmPresets.DefaultApiKeyEnv, orkeon),
                   (UnprefixedApiKey, unprefixed),
                   (OrkeonReference, null),
                   (UnprefixedReference, null),
                   (reference, referenced)))
        {
            var runtime = LlmSettings.ReadDefault(RunnerSettings.ReadConfiguration(settings)).ApiKey;
            var probe = LlmApiKeyResolver.Resolve(file, reference, SystemEnvironmentVariables.Instance).Key;

            Assert.Equal(expected, runtime);
            Assert.Equal(runtime, probe);
        }
    }

    [Fact]
    public async Task The_reference_the_probe_follows_is_the_one_the_runtime_follows()
    {
        // Llm:ApiKeyEnvVar composes like every key: the ORKEON_ variable over the file's.
        var named = "ORKEON_PARITY_" + Guid.NewGuid().ToString("N");
        var overriding = "ORKEON_PARITY_" + Guid.NewGuid().ToString("N");
        var settings = await WriteSettingsAsync(apiKey: null, named);

        using (ProcessVariables.Set(
                   (LlmPresets.DefaultApiKeyEnv, null),
                   (UnprefixedApiKey, null),
                   (OrkeonReference, overriding),
                   (UnprefixedReference, null),
                   (named, "sk-named"),
                   (overriding, "sk-overriding")))
        {
            var runtime = LlmSettings.ReadDefault(RunnerSettings.ReadConfiguration(settings)).ApiKey;

            Assert.Equal("sk-overriding", runtime);
            Assert.Equal(runtime, LlmApiKeyResolver.Resolve(null, named, SystemEnvironmentVariables.Instance).Key);
        }
    }

    /// <summary>
    /// STUDIO-56, decision 1: every spelling the configuration reads as the key, or as the
    /// reference, gives the probe what it gives the run. Red under Linux before the fix — the probe
    /// read the exact name —; green under Windows either way, where these are the same variables.
    /// </summary>
    [Theory]
    [InlineData("ORKEON_LLM__APIKEY", false)]
    [InlineData("orkeon_llm__apikey", false)]
    [InlineData("ORKEON_Llm:ApiKey", false)]
    [InlineData("LLM__APIKEY", false)]
    [InlineData("llm__apikey", false)]
    [InlineData("ORKEON_LLM__APIKEYENVVAR", true)]
    public async Task Every_spelling_a_run_reads_gives_the_probe_the_same_key(string spelling, bool isReference)
    {
        var referenced = "ORKEON_PARITY_" + Guid.NewGuid().ToString("N");
        var settings = await WriteSettingsAsync(apiKey: null, "ORKEON_PARITY_UNSET_" + Guid.NewGuid().ToString("N"));

        using (ProcessVariables.Set(
                   (LlmPresets.DefaultApiKeyEnv, null),
                   (UnprefixedApiKey, null),
                   (OrkeonReference, null),
                   (UnprefixedReference, null),
                   (referenced, "sk-referenced"),
                   (spelling, isReference ? referenced : "sk-spelled")))
        {
            var runtime = LlmSettings.ReadDefault(RunnerSettings.ReadConfiguration(settings)).ApiKey;
            var probe = LlmApiKeyResolver.Resolve(null, null, SystemEnvironmentVariables.Instance).Key;

            Assert.Equal(isReference ? "sk-referenced" : "sk-spelled", runtime);
            Assert.Equal(runtime, probe);
        }
    }

    [Fact]
    public async Task A_reference_in_another_case_than_its_variable_reads_alike_on_both_sides()
    {
        // The variable a reference names is read by its exact name on both sides: found under Windows,
        // where names have no case, absent under Linux and macOS.
        var variable = "ORKEON_PARITY_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var reference = variable.ToLowerInvariant();
        var settings = await WriteSettingsAsync(apiKey: null, reference);

        using (ProcessVariables.Set(
                   (LlmPresets.DefaultApiKeyEnv, null),
                   (UnprefixedApiKey, null),
                   (OrkeonReference, null),
                   (UnprefixedReference, null),
                   (variable, "sk-variable")))
        {
            var runtime = LlmSettings.ReadDefault(RunnerSettings.ReadConfiguration(settings)).ApiKey;

            Assert.Equal(runtime, LlmApiKeyResolver.Resolve(null, reference, SystemEnvironmentVariables.Instance).Key);
        }
    }

    [Fact]
    public async Task Two_spellings_with_two_values_give_the_run_either_one_and_the_probe_a_conflict()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Under Windows two spellings of a name are one variable.");
        var settings = await WriteSettingsAsync(apiKey: null, "ORKEON_PARITY_UNSET_" + Guid.NewGuid().ToString("N"));

        using (ProcessVariables.Set(
                   (LlmPresets.DefaultApiKeyEnv, "sk-a"),
                   ("ORKEON_LLM__APIKEY", "sk-b"),
                   (UnprefixedApiKey, null),
                   (OrkeonReference, null),
                   (UnprefixedReference, null)))
        {
            var runtime = LlmSettings.ReadDefault(RunnerSettings.ReadConfiguration(settings)).ApiKey;
            var probe = LlmApiKeyResolver.Resolve(null, null, SystemEnvironmentVariables.Instance);

            Assert.True(runtime is "sk-a" or "sk-b", "the run reads one of the two values");
            Assert.True(probe.IsConflict);
            Assert.Equal(["ORKEON_LLM__APIKEY", LlmPresets.DefaultApiKeyEnv], probe.ConflictingVariables);
        }
    }

    /// <summary>A settings file whose <c>Llm</c> section holds <paramref name="apiKey"/> and names <paramref name="reference"/>.</summary>
    private async Task<string> WriteSettingsAsync(string? apiKey, string reference)
    {
        var llm = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BaseUrl"] = "https://api.deepseek.com",
            ["Model"] = "deepseek-v4-flash",
            ["ApiKeyEnvVar"] = reference,
        };
        if (apiKey is not null)
            llm["ApiKey"] = apiKey;

        var path = Path.Combine(_root, "appsettings.json");
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(new Dictionary<string, object> { ["Llm"] = llm }),
            TestContext.Current.CancellationToken);
        return path;
    }

    /// <summary>Sets process variables — null removes one — and puts every previous value back when disposed.</summary>
    private sealed class ProcessVariables : IDisposable
    {
        private readonly List<(string Name, string? Previous)> _previous = [];

        public static ProcessVariables Set(params (string Name, string? Value)[] variables)
        {
            var scope = new ProcessVariables();
            try
            {
                foreach (var (name, value) in variables)
                {
                    scope._previous.Add((name, Environment.GetEnvironmentVariable(name)));
                    Environment.SetEnvironmentVariable(name, value);
                }
            }
            catch
            {
                scope.Dispose();
                throw;
            }

            return scope;
        }

        public void Dispose()
        {
            // In reverse: under Windows two spellings are one variable, and the first value read is
            // the one to put back.
            for (var index = _previous.Count - 1; index >= 0; index--)
                Environment.SetEnvironmentVariable(_previous[index].Name, _previous[index].Previous);
        }
    }
}
