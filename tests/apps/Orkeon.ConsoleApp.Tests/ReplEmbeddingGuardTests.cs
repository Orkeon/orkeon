using Orkeon.Hosting;

namespace Orkeon.ConsoleApp.Tests;

/// <summary>FWK final review L3: the REPL never starts its host, so its start calls the embedding model guard itself.</summary>
public sealed class ReplEmbeddingGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-repl-embedding-guard-" + Guid.NewGuid().ToString("N"));

    public ReplEmbeddingGuardTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private Microsoft.Extensions.Hosting.IHost BuildHost()
    {
        var path = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(path, """{ "RaggableTree": { "Enabled": false }, "Orkeon": { "Embeddings": { "Provider": "ollama", "Model": "nomic-embed-text", "Dimension": 768 } } }""");
        return RunnerHost.Build(path, new RunnerMountPlan { CliMounts = [$"{_root}:/workspace:rw"], AllowExternalMounts = true });
    }

    [Fact]
    public async Task A_changed_embedding_model_is_said_on_stderr_and_refuses_the_start()
    {
        using var host = BuildHost();
        var fingerprint = Path.Combine(_root, ".orkeon", "embedding-fingerprint.json");
        Directory.CreateDirectory(Path.GetDirectoryName(fingerprint)!);
        await File.WriteAllTextAsync(fingerprint, """{"Provider":"openai","Model":"text-embedding-3-small","Dimensions":1536}""", TestContext.Current.CancellationToken);
        using var stderr = new StringWriter();

        var exit = await ReplEmbeddingGuard.CheckAsync(host.Services, stderr, TestContext.Current.CancellationToken);

        Assert.Equal(ReplEmbeddingGuard.RefusedExitCode, exit);
        Assert.Contains("embedding model changed", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_recorded_model_lets_the_start_go_on()
    {
        using var host = BuildHost();
        using var stderr = new StringWriter();

        var exit = await ReplEmbeddingGuard.CheckAsync(host.Services, stderr, TestContext.Current.CancellationToken);

        Assert.Null(exit);
        Assert.Equal(string.Empty, stderr.ToString());
    }
}
