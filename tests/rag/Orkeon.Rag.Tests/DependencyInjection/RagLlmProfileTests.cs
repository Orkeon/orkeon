using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.DependencyInjection;
using Orkeon.Rag.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Rag.Tests.DependencyInjection;

/// <summary>
/// GAP-19, decision 2 — the RAG subsystem runs on the host profile <c>Orkeon:Rag:LlmProfile</c>
/// names: grounded generation, query transformers, the listwise reranker, the corrective graph's
/// evaluator and groundedness checker, the <c>llm</c> classifier and the evaluation judge. Every one
/// of them took the container's <see cref="IChatClient"/> — the default profile — whatever the
/// host said. The profile is resolved when a RAG component is first built, never at start-up, and
/// a name the host does not offer fails with the list of the ones it does.
/// </summary>
public sealed class RagLlmProfileTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Host : IDisposable
    {
        public Host(IReadOnlyDictionary<string, string?> settings)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            Profiles = new StubLlmProfileRegistry(("a", A), ("b", B));

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
                .AddMount("/kb")
                .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create)
                .AddFile("/kb/hours.md", "The library opens at nine in the morning and closes at six.")
                .AddFile("/kb/loans.md", "A book may be borrowed for three weeks and renewed once."));
            services.AddSingleton<IDocumentStore>(new FakeDocumentStore());
            services.AddSingleton<IEmbeddingProvider>(new FakeEmbeddingProvider());
            services.AddSingleton<IChatClient>(Default);
            services.AddSingleton<ILlmProfileRegistry>(Profiles);
            services.AddOrkeonRag(configuration);
            Services = services.BuildServiceProvider();
        }

        public FakeChatClient Default { get; } = new() { ResponseText = "The library opens at nine [1]." };

        public FakeChatClient A { get; } = new() { ResponseText = "The library opens at nine [1]." };

        public FakeChatClient B { get; } = new() { ResponseText = "The library opens at nine [1]." };

        public StubLlmProfileRegistry Profiles { get; }

        public ServiceProvider Services { get; }

        public async Task IngestAsync() =>
            await Services.GetRequiredService<IIngestionPipeline>().IngestAsync(new IngestionRequest
            {
                Collection = "library",
                Sources = [new SourceDescriptor { Location = "/kb/hours.md" }, new SourceDescriptor { Location = "/kb/loans.md" }],
            }, Ct);

        public Task<RagAnswer> AskAsync() =>
            Services.GetRequiredService<IRagPipeline>().QueryAsync(
                new RagQuery { Text = "When does the library open?", Collection = "library" }, Ct);

        public void Dispose()
        {
            Services.Dispose();
            Default.Dispose();
            A.Dispose();
            B.Dispose();
        }
    }

    private static Dictionary<string, string?> Settings(params (string Key, string Value)[] entries) =>
        entries.ToDictionary(e => e.Key, e => (string?)e.Value);

    private static string Prompts(FakeChatClient client) =>
        string.Join("\n", client.Calls.SelectMany(call => call).Select(message => message.Text));

    [Fact]
    public async Task Generation_runs_on_the_profile_the_host_names()
    {
        using var host = new Host(Settings(("Orkeon:Rag:LlmProfile", "b")));
        await host.IngestAsync();

        var answer = await host.AskAsync();

        Assert.Equal("The library opens at nine [1].", answer.Text);
        Assert.Equal(1, host.B.CallCount);
        Assert.Equal(0, host.Default.CallCount);
        Assert.Equal(0, host.A.CallCount);
    }

    [Fact]
    public async Task Without_a_profile_the_rag_stays_on_the_hosts_default()
    {
        using var host = new Host(Settings());
        await host.IngestAsync();

        await host.AskAsync();

        Assert.Equal(1, host.Default.CallCount);
        Assert.Equal(0, host.A.CallCount + host.B.CallCount);
        Assert.Empty(host.Profiles.Resolved);
    }

    [Theory]
    [InlineData("Orkeon:Rag:QueryTransform:Mode", "multi-query")]
    [InlineData("Orkeon:Rag:QueryTransform:Mode", "rag-fusion")]
    [InlineData("Orkeon:Rag:QueryTransform:Mode", "hyde")]
    [InlineData("Orkeon:Rag:Rerank:Kind", "llm")]
    [InlineData("Orkeon:Rag:Groundedness:Enabled", "true")]
    [InlineData("Orkeon:Rag:Profile", "corrective")]
    public async Task Every_stage_that_asks_a_model_asks_the_profile(string key, string value)
    {
        var settings = Settings(("Orkeon:Rag:LlmProfile", "b"), (key, value));
        if (key == "Orkeon:Rag:Rerank:Kind")
            settings["Orkeon:Rag:Rerank:Enabled"] = "true";
        using var host = new Host(settings);
        await host.IngestAsync();

        await host.AskAsync();

        // The stage's own call, then the generation: both on the profile, none on the default.
        Assert.True(host.B.CallCount >= 2, $"{key}={value}: {host.B.CallCount} call(s) on the profile.\n{Prompts(host.B)}");
        Assert.Equal(0, host.Default.CallCount);
        Assert.Equal(0, host.A.CallCount);
    }

    [Fact]
    public async Task The_llm_classifier_asks_the_profile()
    {
        using var host = new Host(Settings(("Orkeon:Rag:LlmProfile", "b"), ("Orkeon:Rag:QueryRouting:Classifier", "llm")));

        await host.Services.GetRequiredService<IQueryComplexityClassifier>().ClassifyAsync("When does the library open?", Ct);

        Assert.Equal(1, host.B.CallCount);
        Assert.Equal(0, host.Default.CallCount);
    }

    [Fact]
    public async Task The_evaluation_judge_asks_the_profile()
    {
        using var host = new Host(Settings(("Orkeon:Rag:LlmProfile", "b")));
        await host.IngestAsync();
        host.B.ScriptedResponses.Enqueue("The library opens at nine [1].");
        host.B.ScriptedResponses.Enqueue("""{"groundedness": 1, "answer_relevance": 1}""");

        var report = await host.Services.GetRequiredService<IRagEvaluator>().RunAsync(
            new RagEvalDataset
            {
                Name = "library",
                Cases = [new RagEvalCase { Id = "q-1", Question = "When does the library open?", ExpectedSubstrings = ["nine"] }],
            },
            new RagEvalOptions { Collection = "library", UseLlmJudge = true },
            Ct);

        Assert.Equal(RagJudgeMode.Llm, Assert.Single(report.Cases).Judge);
        Assert.Contains("Score the ANSWER", Prompts(host.B), StringComparison.Ordinal);
        Assert.Equal(0, host.Default.CallCount);
    }

    [Fact]
    public async Task The_profile_is_resolved_at_first_use_never_when_a_crew_merely_loads()
    {
        using var host = new Host(Settings(("Orkeon:Rag:LlmProfile", "b")));

        // What every crew resolves when it loads (GAP-02): neither asks a model.
        Assert.NotNull(host.Services.GetRequiredService<IRagCollectionsBootstrapper>());
        Assert.NotNull(host.Services.GetRequiredService<IKnowledgeContextAugmenter>());
        Assert.Empty(host.Profiles.Resolved);

        await host.IngestAsync();
        await host.AskAsync();

        Assert.Contains("b", host.Profiles.Resolved);
    }

    private static IConfiguration Configuration(string? profile) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [RagLlm.ProfileKey] = profile,
        }).Build();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("default")]
    [InlineData("b")]
    [InlineData(" B ")]
    public void The_start_up_check_passes_a_profile_the_host_offers_without_building_it(string? profile)
    {
        using var a = new FakeChatClient();
        using var b = new FakeChatClient();
        var registry = new StubLlmProfileRegistry(("a", a), ("b", b));

        RagLlm.EnsureProfileIsKnown(Configuration(profile), registry);

        Assert.Empty(registry.Resolved);
    }

    [Fact]
    public void The_start_up_check_refuses_a_profile_the_host_does_not_offer_listing_the_known_ones()
    {
        using var a = new FakeChatClient();
        var registry = new StubLlmProfileRegistry(("a", a));

        var error = Assert.Throws<InvalidOperationException>(() => RagLlm.EnsureProfileIsKnown(Configuration("claude"), registry));

        Assert.Equal("Orkeon:Rag:LlmProfile", RagLlm.ProfileKey);
        Assert.Contains("Orkeon:Rag:LlmProfile names the LLM profile 'claude'", error.Message, StringComparison.Ordinal);
        Assert.Contains("Known profiles: default, a.", error.Message, StringComparison.Ordinal);
        Assert.Contains(
            "Known profiles: default.",
            Assert.Throws<InvalidOperationException>(() => RagLlm.EnsureProfileIsKnown(Configuration("claude"), profiles: null)).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_profile_the_host_does_not_offer_fails_the_first_use_listing_the_known_ones()
    {
        using var host = new Host(Settings(("Orkeon:Rag:LlmProfile", "nope")));
        await host.IngestAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(host.AskAsync);

        Assert.Contains("Orkeon:Rag:LlmProfile", error.Message, StringComparison.Ordinal);
        Assert.Contains("'nope'", error.Message, StringComparison.Ordinal);
        Assert.Contains("Known profiles: default, a, b.", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, host.Default.CallCount);
    }
}
