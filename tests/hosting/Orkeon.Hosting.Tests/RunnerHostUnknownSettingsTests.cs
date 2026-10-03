using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// GAP-40, decision 5 — a key the host does not know refuses its start, naming it: a section name
/// under <c>Orkeon:</c> or a group that is none (the closest proposed), a key a section the host reads
/// does not carry, a key GAP-08 removed (with its migration). It used to be read as absent, and the
/// setting the operator meant ran on its default. What stays open: the root, <c>Logging</c> beyond its
/// levels, the keys of a dictionary, and the keys of a section another host reads.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerHostUnknownSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-unknown-settings-" + Guid.NewGuid().ToString("N"));

    public RunnerHostUnknownSettingsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private string Settings(params (string Key, string Value)[] values)
    {
        var all = new Dictionary<string, string> { ["RaggableTree:Enabled"] = "false" };
        foreach (var (key, value) in values)
            all[key] = value;

        var path = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(path, SettingsJson.Of(all));
        return path;
    }

    private static RunnerSettingsException Refusal(string settings) =>
        Assert.Throws<RunnerSettingsException>(() => RunnerHost.Build(settings, new RunnerMountPlan()));

    [Theory]
    [InlineData("Orkeon:Guardian:Enabeld", "true", "Orkeon:Guardian:Enabeld", "Orkeon:Guardian:Enabled")]
    [InlineData("Orkeon:Guardain:Enabled", "true", "Orkeon:Guardain", "Orkeon:Guardian")]
    [InlineData("Orkeon:Rag:Ingestion:ChunkSize", "512", "Orkeon:Rag:Ingestion:ChunkSize", "DefaultChunkingStrategy")]
    [InlineData("Orkeon:CodeSandbox:Dockr:PullImageOnStartup", "true", "Orkeon:CodeSandbox:Dockr", "Orkeon:CodeSandbox:Docker")]
    [InlineData("Llm:Provider", "deepseek", "Llm:Provider", "BaseUrl")]
    [InlineData("Llm:Profiles:a:Modle", "gpt", "Llm:Profiles:a:Modle", "Llm:Profiles:a:Model")]
    [InlineData("Security:Promt:Policy", "Block", "Security:Promt", "Security:Prompt")]
    [InlineData("orkeon:guardain:enabled", "true", "guardain", "Orkeon:Guardian")]
    public void An_unknown_key_is_refused_by_its_name(string key, string value, string named, string proposed)
    {
        var error = Refusal(Settings((key, value)));

        Assert.Contains(named, error.Message, StringComparison.Ordinal);
        Assert.Contains(proposed, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Orkeon:Rag:ConnectionString", "Data Source=/output/rag.db", "Orkeon:Rag:ConnectionString", "Orkeon:Sqlite:ConnectionString")]
    [InlineData("Orkeon:Rag:ProviderOptions:Host", "localhost", "Orkeon:Rag:ProviderOptions", "Orkeon:Sqlite")]
    [InlineData("Memory:ConnectionString", "localhost:6379", "Memory:ConnectionString", "Orkeon:Redis:ConnectionString")]
    [InlineData("Orkeon:Pinecone:Environment", "us-east1-gcp", "Orkeon:Pinecone:Environment", "Orkeon:Pinecone:Host")]
    public void A_key_GAP_08_removed_is_refused_with_its_migration(string key, string value, string named, string migration)
    {
        var error = Refusal(Settings((key, value)));

        Assert.Contains(named, error.Message, StringComparison.Ordinal);
        Assert.Contains(migration, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Llm:Profiles:mon-profil:Model", "qwen3")]
    [InlineData("Orkeon:Rag:Ingestion:ManifestDirectory", "/output/manifests")]
    [InlineData("Orkeon:CodeSandbox:Docker:PullImageOnStartup", "true")]
    [InlineData("Orkeon:Consensus:RoleWeights:Reviewer", "2")]
    [InlineData("MyApp:Foo", "bar")]
    [InlineData("ORKEON_DEBUG", "1")]
    [InlineData("DEBUG", "1")]
    [InlineData("Logging:LogLevel:MyNamespace", "Warning")]
    [InlineData("Logging:Console:FormatterName", "simple")]
    [InlineData("Secrets:Anything", "x")]
    [InlineData("Orkeon:Host:Crews:0:Name", "support")]
    [InlineData("Orkeon:Host:RunTimeoutt", "00:10:00")]
    public void A_key_the_host_accepts_builds_it(string key, string value)
    {
        using var host = RunnerHost.Build(Settings((key, value), ("Llm:Profiles:mon-profil:BaseUrl", "http://localhost:11434")), new RunnerMountPlan());

        Assert.NotNull(host);
    }

    /// <summary>
    /// Configuration keys are case-insensitive, and the configuration reports each one as it was
    /// written: <c>ORKEON_LLM__APIKEY</c> is read as <c>Llm:ApiKey</c> under a root child <c>LLM</c>,
    /// a file's <c>"llm": { "apikey": … }</c> under <c>llm</c>. The shapes, the section names and the
    /// retired keys are compared without case: a key the run reads is never refused for its case.
    /// </summary>
    [Theory]
    [InlineData("""{ "RaggableTree": { "Enabled": false }, "llm": { "apikey": "sk-test", "baseurl": "http://localhost:11434" } }""")]
    [InlineData("""{ "raggabletree": { "enabled": false }, "ORKEON": { "GUARDIAN": { "ENABLED": true }, "rag": { "retrieval": { "topk": 3 } } } }""")]
    public void A_key_written_in_another_case_is_read_and_accepted(string json)
    {
        var path = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(path, json);

        using var host = RunnerHost.Build(path, new RunnerMountPlan());

        Assert.NotNull(host);
    }

    [Fact]
    public void An_upper_case_environment_variable_is_read_and_accepted()
    {
        Environment.SetEnvironmentVariable("ORKEON_LLM__APIKEY", "sk-test");
        Environment.SetEnvironmentVariable("ORKEON_ORKEON__GUARDIAN__ENABLED", "true");
        try
        {
            using var host = RunnerHost.Build(Settings(), new RunnerMountPlan());

            Assert.Equal("sk-test", host.Services.GetRequiredService<IConfiguration>()["Llm:ApiKey"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ORKEON_LLM__APIKEY", null);
            Environment.SetEnvironmentVariable("ORKEON_ORKEON__GUARDIAN__ENABLED", null);
        }
    }

    [Fact]
    public void The_keys_of_a_dictionary_are_open()
    {
        var settings = Settings(
            ("MCP:Servers:fs:Command", "npx"),
            ("MCP:Servers:fs:Env:MY_VAR", "1"));

        using var host = RunnerHost.Build(settings, new RunnerMountPlan());

        Assert.NotNull(host);
    }
}
