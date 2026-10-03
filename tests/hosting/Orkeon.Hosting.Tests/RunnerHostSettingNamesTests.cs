namespace Orkeon.Hosting.Tests;

/// <summary>
/// GAP-40, decision 6 — a name the host resolves to a component is checked at its start against the
/// names it knows, the list in the refusal. It used to be checked at the first use — the first RAG
/// query or ingestion, the first classification —, or never: an unknown <c>Memory:Provider</c>, or
/// <c>lancedb</c> without its endpoint, ran on the volatile in-memory provider, and an
/// <c>Orkeon:Embeddings:Provider</c> written wrong became OpenAI.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerHostSettingNamesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-setting-names-" + Guid.NewGuid().ToString("N"));

    public RunnerHostSettingNamesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private string Settings(string key, string value)
    {
        var path = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(path, SettingsJson.Of(new Dictionary<string, string> { ["RaggableTree:Enabled"] = "false", [key] = value }));
        return path;
    }

    [Theory]
    [InlineData("Memory:Provider", "redsi", "Memory:Provider", "sqlite")]
    [InlineData("Memory:Provider", "lancedb", "Memory:Provider", "Orkeon:LanceDb:Endpoint")]
    [InlineData("Memory:Provider", "pinecone", "Memory:Provider", "Orkeon:Pinecone:ApiKey")]
    [InlineData("Orkeon:Rag:Provider", "mongo", "Orkeon:Rag:Provider", "chromadb")]
    [InlineData("Orkeon:Rag:Rerank:Kind", "cohere", "Orkeon:Rag:Rerank:Kind", "listwise")]
    [InlineData("Orkeon:Rag:QueryTransform:Mode", "fusion", "Orkeon:Rag:QueryTransform:Mode", "rag-fusion")]
    [InlineData("Orkeon:Rag:Ingestion:DefaultChunkingStrategy", "paragraph", "Orkeon:Rag:Ingestion:DefaultChunkingStrategy", "recursive")]
    [InlineData("Orkeon:Rag:Context:Ordering", "middle", "Orkeon:Rag:Context:Ordering", "edges")]
    [InlineData("Orkeon:Rag:QueryRouting:Classifier", "smart", "Orkeon:Rag:QueryRouting:Classifier", "heuristic")]
    [InlineData("Orkeon:Embeddings:Provider", "olama", "Orkeon:Embeddings:Provider", "ollama")]
    [InlineData("Orkeon:Sqlite:TableName", "memory-items", "Orkeon:Sqlite:TableName", "underscores")]
    [InlineData("Orkeon:Cli:Session:ContextWindowTokens", "0", "Orkeon:Cli:Session:ContextWindowTokens", "above zero")]
    public void A_name_the_host_does_not_know_is_refused_with_the_known_ones(string key, string value, string named, string listed)
    {
        var settings = Settings(key, value);

        var error = Assert.Throws<RunnerSettingsException>(() => RunnerHost.Build(settings, new RunnerMountPlan()));

        Assert.Contains(named, error.Message, StringComparison.Ordinal);
        Assert.Contains(listed, error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A C# host — <see cref="RunnerHost"/> alone — has no ONNX reranker: the <c>balanced</c> profile,
    /// which reranks with it, is refused at the start, naming <c>onnx</c>; the binaries add it.
    /// </summary>
    [Fact]
    public void The_balanced_profile_is_refused_by_a_host_without_the_onnx_reranker()
    {
        var settings = Settings("Orkeon:Rag:Profile", "balanced");

        var error = Assert.Throws<RunnerSettingsException>(() => RunnerHost.Build(settings, new RunnerMountPlan()));

        Assert.Contains("onnx", error.Message, StringComparison.Ordinal);
        Assert.Contains("balanced", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Memory:Provider", "SQLite")]
    [InlineData("Memory:Provider", "in-memory")]
    [InlineData("Orkeon:Rag:Provider", "chroma")]
    [InlineData("Orkeon:Rag:Rerank:Kind", "LLM")]
    [InlineData("Orkeon:Embeddings:Provider", "Ollama")]
    public void A_known_name_builds_the_host_whatever_its_case(string key, string value)
    {
        using var host = RunnerHost.Build(Settings(key, value), new RunnerMountPlan());

        Assert.NotNull(host);
    }
}
