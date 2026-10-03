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
            var probe = LlmApiKeyResolver.Resolve(file, reference);

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
            Assert.Equal(runtime, LlmApiKeyResolver.Resolve(null, named));
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
            foreach (var (name, previous) in _previous)
                Environment.SetEnvironmentVariable(name, previous);
        }
    }
}
