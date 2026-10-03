namespace Orkeon.Hosting.Tests;

/// <summary>
/// GAP-40, decision 4 — the <c>LlmLogging</c> section, read with <c>--llm-log</c>: a value the host
/// cannot read refuses the start, naming its key. Its switches and its size limit used to fall back
/// on their defaults in silence — a log meant to be cut at 10 000 characters was written whole.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerHostLlmLoggingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-llm-logging-" + Guid.NewGuid().ToString("N"));

    public RunnerHostLlmLoggingTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private RunnerMountPlan WithExchangeLog()
    {
        var logs = Directory.CreateDirectory(Path.Combine(_root, "llm-logs")).FullName;
        return new RunnerMountPlan
        {
            InternalMounts = [$"{logs}:{Orkeon.Constants.FileSystem.RunnerVirtualRoots.LlmLogs}:rw"],
            AllowExternalMounts = true,
            LlmLogVirtualPath = Orkeon.Constants.FileSystem.RunnerVirtualRoots.LlmLogs,
        };
    }

    private string Settings(string key, string value)
    {
        var path = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(path, SettingsJson.Of(new Dictionary<string, string> { ["RaggableTree:Enabled"] = "false", [key] = value }));
        return path;
    }

    [Theory]
    [InlineData("LlmLogging:MaxBodyLengthChars", "10k")]
    [InlineData("LlmLogging:FullEmbeddingLog", "maybe")]
    [InlineData("LlmLogging:LogStreamingExchanges", "sometimes")]
    [InlineData("LlmLogging:MaxBodyLenghtChars", "10000")]
    public void A_value_or_a_key_the_exchange_log_cannot_honour_is_refused_by_its_key(string key, string value)
    {
        var settings = Settings(key, value);

        var error = Assert.Throws<RunnerSettingsException>(() => RunnerHost.Build(settings, WithExchangeLog()));

        Assert.Contains(key, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Readable_values_build_the_host()
    {
        using var host = RunnerHost.Build(Settings("LlmLogging:MaxBodyLengthChars", "10000"), WithExchangeLog());

        Assert.NotNull(host);
    }
}
