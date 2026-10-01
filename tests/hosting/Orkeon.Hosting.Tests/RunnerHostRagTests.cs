using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.FileSystem;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// GAP-02: every host built on <see cref="RunnerHost"/> — <c>orkeon run</c> in all its forms,
/// <c>orkeon-host</c> — registers the RAG subsystem. A YAML crew's <c>rag:</c> block is
/// ingested when the crew loads and its agents' <c>knowledge:</c> reaches their prompts;
/// before, the runner logged that the subsystem was missing and the agent answered without
/// a single excerpt. Offline: a bag-of-words embedding double and a capturing chat client.
/// Joins <see cref="ConsoleSerialCollection"/> because it redirects the process-global console.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerHostRagTests : IDisposable
{
    private sealed class TestOptions : RunnerOptionsBase { }

    private const string Faq =
        "Refund policy: customers may request a full refund within 30 days of purchase.\n\n" +
        "Battery: the device lasts 12 hours on a full charge.";

    private const string KnowledgeCrew = """
        name: "support-crew"
        goal: "Answer customer questions from the product FAQ"
        process: "sequential"
        rag:
          collections:
            produits:
              sources: ["/kb/faq.md"]
        agents:
          support:
            role: "Support agent"
            goal: "Answer questions from the knowledge base"
            backstory: "A careful support agent."
            maxIter: 1
            knowledge: [produits]
        tasks:
          answer:
            description: "How many days do customers have to request a refund?"
            expectedOutput: "A grounded answer with citations."
            agent: "support"
        """;

    private const string PlainCrew = """
        name: "plain-crew"
        goal: "A crew without knowledge"
        process: "sequential"
        agents:
          worker:
            role: "Worker"
            goal: "Work"
            backstory: "A minimal test agent."
            maxIter: 1
        tasks:
          work:
            description: "Do the work."
            expectedOutput: "The work."
            agent: "worker"
        """;

    private readonly string _root;
    private readonly string _kb;

    public RunnerHostRagTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "orkeon-hosting-rag-" + Guid.NewGuid().ToString("N"));
        _kb = Path.Combine(_root, "kb");
        Directory.CreateDirectory(_kb);
        File.WriteAllText(Path.Combine(_kb, "faq.md"), Faq);
        File.WriteAllText(Path.Combine(_root, "knowledge.yaml"), KnowledgeCrew);
        File.WriteAllText(Path.Combine(_root, "plain.yaml"), PlainCrew);
        WriteSettings("{ \"RaggableTree\": { \"Enabled\": false } }");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort temp cleanup */ }
    }

    [Fact]
    public void A_runner_host_resolves_the_rag_bootstrapper_and_the_knowledge_augmenter()
    {
        using var host = RunnerHost.Build(
            Path.Combine(_root, "appsettings.json"),
            new RunnerMountPlan { CliMounts = [$"{FileSystemMount.Quote(_kb)}:/kb:ro"] });

        Assert.NotNull(host.Services.GetService<IRagCollectionsBootstrapper>());
        Assert.NotNull(host.Services.GetService<IKnowledgeContextAugmenter>());
        var tools = host.Services.GetServices<Orkeon.Domain.Tools.IBaseTool>().Select(t => t.Name).ToList();
        Assert.Contains("rag_search", tools);
        Assert.Contains("rag_ingest", tools);
        Assert.Contains("rag_eval", tools);
    }

    [Fact]
    public async Task A_yaml_crew_with_rag_and_knowledge_gets_the_cited_excerpts_in_its_agent_prompt()
    {
        using var chat = new CapturingChatClient();

        var (exit, stderr) = await RunAsync("knowledge.yaml", chat);

        Assert.True(exit == 0, stderr);
        var prompt = Assert.Single(chat.Prompts, p => p.Contains("## Knowledge Context", StringComparison.Ordinal));
        Assert.Contains("[1] (collection: produits, source: /kb/faq.md", prompt, StringComparison.Ordinal);
        Assert.Contains("full refund within 30 days", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_shipped_crew_yaml_example_runs_with_its_knowledge_under_the_runner()
    {
        // The acceptance case of GAP-02: examples/rag/crew-yaml, run the way the README
        // says (`orkeon run crew.yaml --mount data:/kb:ro`), ingests its three files and
        // grounds the agent's prompt on them.
        var example = Path.Combine(RepositoryRoot(), "examples", "rag", "crew-yaml");
        using var chat = new CapturingChatClient();
        var opts = new TestOptions
        {
            ConfigPath = Path.Combine(example, "crew.yaml"),
            Mounts = [$"{FileSystemMount.Quote(Path.Combine(example, "data"))}:/kb:ro"],
            SettingsPath = Path.Combine(_root, "appsettings.json"),
            AllowExternalMounts = true,
        };

        var (exit, stderr) = await RunAsync(opts, chat);

        Assert.True(exit == 0, stderr);
        var prompt = Assert.Single(chat.Prompts, p => p.Contains("## Knowledge Context", StringComparison.Ordinal));
        Assert.Contains("[1] (collection: product-kb, source: /kb/", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_crew_without_knowledge_runs_even_with_an_unknown_rag_store_alias()
    {
        WriteSettings("{ \"RaggableTree\": { \"Enabled\": false }, \"Orkeon\": { \"Rag\": { \"Provider\": \"mongodb\" } } }");
        using var chat = new CapturingChatClient();

        var (exit, stderr) = await RunAsync("plain.yaml", chat);

        Assert.True(exit == 0, stderr);
        Assert.NotEmpty(chat.Prompts);
    }

    [Fact]
    public async Task Validate_still_ingests_nothing()
    {
        var ingestion = new CountingIngestionPipeline();
        var opts = Options("knowledge.yaml");

        var origOut = Console.Out;
        var origErr = Console.Error;
        using var stdout = new StringWriter(new StringBuilder());
        using var stderr = new StringWriter(new StringBuilder());
        Console.SetOut(stdout);
        Console.SetError(stderr);
        int exit;
        try
        {
            exit = await RunnerExecution.RunValidateAsync(
                opts,
                "Orkeon.Hosting.Tests",
                (_, services) => services.AddSingleton<IIngestionPipeline>(ingestion),
                TestContext.Current.CancellationToken);
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }

        Assert.True(exit == 0, stderr.ToString());
        Assert.Equal(0, ingestion.Calls);
    }

    private void WriteSettings(string json) =>
        File.WriteAllText(Path.Combine(_root, "appsettings.json"), json);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Orkeon.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Orkeon.sln not found above the test binaries.");
    }

    private TestOptions Options(string crewFile) => new()
    {
        ConfigPath = Path.Combine(_root, crewFile),
        Mounts = [$"{FileSystemMount.Quote(_kb)}:/kb:ro"],
        AllowExternalMounts = true,
    };

    private Task<(int Exit, string Stderr)> RunAsync(string crewFile, CapturingChatClient chat) =>
        RunAsync(Options(crewFile), chat);

    private static async Task<(int Exit, string Stderr)> RunAsync(TestOptions options, CapturingChatClient chat)
    {
        var origOut = Console.Out;
        var origErr = Console.Error;
        using var stdout = new StringWriter(new StringBuilder());
        using var stderr = new StringWriter(new StringBuilder());
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var exit = await RunnerExecution.RunOneShotAsync(
                options,
                "Orkeon.Hosting.Tests",
                (_, services) =>
                {
                    services.AddSingleton<IEmbeddingProvider>(new LexicalEmbeddingProvider());
                    services.AddSingleton<IChatClient>(chat);
                },
                TestContext.Current.CancellationToken);
            return (exit, stderr.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }

    /// <summary>Counts ingestion requests; never ingests anything.</summary>
    private sealed class CountingIngestionPipeline : IIngestionPipeline
    {
        private int _calls;

        public int Calls => _calls;

        public Task<IngestionReport> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new IngestionReport { Collection = request.Collection });
        }
    }
}
