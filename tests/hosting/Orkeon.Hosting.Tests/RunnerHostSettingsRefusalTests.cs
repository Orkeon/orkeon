namespace Orkeon.Hosting.Tests;

/// <summary>
/// GAP-35 — every setting <see cref="RunnerHost.Build"/> refuses comes out as one type,
/// <see cref="RunnerSettingsException"/>, whose message names the key — or the file and the
/// place in it. The entry points translate that type (exit 1 for <c>orkeon</c>, 78 for
/// <c>orkeon-host</c>); before, a retired key, an unconvertible value, an unreadable file or a
/// malformed address each left the build as its own exception, and most of them crashed the
/// program. The refusals the host already raised — invalid <c>Llm:Profiles</c>, an unknown
/// <c>Orkeon:Rag:LlmProfile</c>, <c>MCP:EnableServer</c>, mounts — are held by their own classes.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerHostSettingsRefusalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-settings-refusal-" + Guid.NewGuid().ToString("N"));

    public RunnerHostSettingsRefusalTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private string Settings(string json)
    {
        var path = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(path, json);
        return path;
    }

    private RunnerSettingsException Refusal(string json)
    {
        var settings = Settings(json);
        return Assert.Throws<RunnerSettingsException>(() => RunnerHost.Build(settings, new RunnerMountPlan()));
    }

    [Fact]
    public void A_key_the_RaggableTree_section_no_longer_carries_is_refused_by_its_name()
    {
        var error = Refusal("""{ "RaggableTree": { "Exclude": ["bin"] } }""");

        Assert.Contains("RaggableTree:Exclude", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_the_binder_cannot_convert_is_refused_by_its_key()
    {
        var error = Refusal("""{ "RaggableTree": { "Enabled": false }, "Orkeon": { "CrewFactory": { "StrictTools": "maybe" } } }""");

        Assert.Contains("Orkeon:CrewFactory:StrictTools", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_settings_file_that_is_not_JSON_is_refused_naming_the_file_and_the_line()
    {
        var error = Refusal("""
            {
              "Llm": { "Model": "gpt" ,, }
            }
            """);

        Assert.Contains(Path.Combine(_root, "appsettings.json"), error.Message, StringComparison.Ordinal);
        Assert.Contains("line 2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_embedding_address_that_is_no_address_is_refused_by_its_key()
    {
        var error = Refusal("""{ "RaggableTree": { "Embedding": { "Provider": "Ollama", "BaseUrl": "pas une url" } } }""");

        Assert.Contains("RaggableTree:Embedding:BaseUrl", error.Message, StringComparison.Ordinal);
        Assert.Contains("pas une url", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_OTLP_endpoint_that_is_no_address_is_refused_by_its_key()
    {
        var error = Refusal("""{ "RaggableTree": { "Enabled": false }, "Telemetry": { "OtlpEndpoint": "::" } }""");

        Assert.Contains("Telemetry:OtlpEndpoint", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "RaggableTree": { "Enabled": false }, "Telemetry": { "ExportToConsole": true } }""", "Telemetry:ExportToConsole")]
    [InlineData("""{ "RaggableTree": { "Enabled": false }, "Telemetry": { "ExportToConsole": false } }""", "Telemetry:ExportToConsole")]
    [InlineData("""{ "RaggableTree": { "Enabled": false }, "Telemetry": { "PrometheusEndpoint": true } }""", "Telemetry:PrometheusEndpoint")]
    public void A_removed_telemetry_key_is_refused_naming_what_replaces_it(string json, string key)
    {
        var error = Refusal(json);

        Assert.Contains(key, error.Message, StringComparison.Ordinal);
        Assert.Contains("Telemetry:OtlpEndpoint", error.Message, StringComparison.Ordinal);
        Assert.Contains("OTEL_EXPORTER_OTLP_ENDPOINT", error.Message, StringComparison.Ordinal);
    }
}
