using Orkeon.Hosting.Tests.Doubles;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// FWK final review L3: the runners never start the host, so the embedding model guard (FWK-25) runs
/// from <see cref="RunnerHost.Build"/> itself, and a changed model refuses the run.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerHostEmbeddingGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-embedding-guard-" + Guid.NewGuid().ToString("N"));

    public RunnerHostEmbeddingGuardTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private const string Embeddings = """{ "RaggableTree": { "Enabled": false }, "Orkeon": { "Embeddings": { "Provider": "ollama", "Model": "nomic-embed-text", "Dimension": 768 } } }""";

    private string Fingerprint => Path.Combine(_root, ".orkeon", "embedding-fingerprint.json");

    private void Build(string json)
    {
        var path = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(path, json);
        var original = Console.Error;
        using var muted = new StringWriter();
        Console.SetError(muted);
        try
        {
            using var host = RunnerHost.Build(
                path,
                new RunnerMountPlan { CliMounts = [$"{_root}:/workspace:rw"], AllowExternalMounts = true });
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    public void A_changed_embedding_model_refuses_the_run_without_the_host_being_started()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Fingerprint)!);
        File.WriteAllText(Fingerprint, """{"Provider":"openai","Model":"text-embedding-3-small","Dimensions":1536}""");

        var refusal = Assert.Throws<RunnerSettingsException>(() => Build(Embeddings));

        Assert.Contains("embedding model changed", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_first_run_records_the_fingerprint_and_the_next_one_with_the_same_model_passes()
    {
        Build(Embeddings);
        Assert.True(File.Exists(Fingerprint));

        Build(Embeddings);
    }

    [Fact]
    public void A_host_without_an_embeddings_section_writes_no_fingerprint()
    {
        Build("""{ "RaggableTree": { "Enabled": false } }""");

        Assert.False(File.Exists(Fingerprint));
    }
}
