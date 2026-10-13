using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Constants.Orchestration;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Application.Telemetry;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Telemetry;
using CrewOutput = Orkeon.Application.Interfaces.Services.CrewOutput;

namespace Orkeon.Infrastructure.Tests.Orchestration;

/// <summary>
/// GAP-30 — a YAML crew with <c>memory: true</c> through the host's own composition
/// (<c>AddOrkeonApplication</c> + <c>AddOrkeonInfrastructure</c>): refused before its first LLM call
/// when its memory cannot embed, and, with an embedder, remembering its first run in the prompt of
/// its second. The model is a recorder; the store is the host's default one (In-Memory).
/// </summary>
public sealed class CrewMemoryKickoffTests
{
    private const string RememberingCrew = """
        name: news-desk
        goal: Publish the AI news summary
        process: sequential
        memory: true
        agents:
          analyst:
            role: Analyst
            goal: Summarize the news
        tasks:
          summary:
            description: Summarize this week's AI news for the newsletter
            expected_output: Five bullet points about the AI news
            agent: analyst
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServiceProvider Host(MockLlmProvider vendor, IEmbeddingProvider? embedder, Dictionary<string, string?>? settings = null, Orkeon.Domain.Memory.IMemoryProvider? memory = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build());
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        if (embedder is not null)
            services.AddSingleton(embedder);
        services.AddOrkeonLlmProvider(_ => vendor, LlmConfig.Create("host-model"));
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        if (memory is not null)
            services.AddSingleton(memory);
        return services.BuildServiceProvider();
    }

    private static async Task<CrewOutput> RunAsync(ServiceProvider container, string yaml)
    {
        await using var scope = container.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var config = await sp.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync(yaml, Ct);
        var crew = await sp.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, Ct);
        return await sp.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(
            crew.Id, new Orkeon.Application.Interfaces.Services.CrewInput("news", new Dictionary<string, object>()), Ct);
    }

    /// <summary>A vendor answering <paramref name="answers"/> in turn, recording every prompt.</summary>
    private static MockLlmProvider Vendor(List<string> prompts, params string[] answers)
    {
        var vendor = new MockLlmProvider { Name = "vendor" };
        var turn = 0;
        vendor.SetChatFunc((messages, _) =>
        {
            prompts.Add(string.Join("\n", messages.Select(m => m.Content)));
            return new LlmResponse { Content = answers[Math.Min(turn++, answers.Length - 1)], PromptTokens = 3, CompletionTokens = 1, TokensUsed = 4 };
        });
        return vendor;
    }

    [Fact]
    public async Task A_crew_with_memory_and_no_embedder_fails_before_its_first_llm_call_saying_why()
    {
        var prompts = new List<string>();
        await using var container = Host(Vendor(prompts, "never asked"), embedder: null);

        var output = await RunAsync(container, RememberingCrew);

        Assert.False(output.Succeeded);
        Assert.Contains("news-desk", output.Error, StringComparison.Ordinal);
        Assert.Contains("no semantic embedding provider is configured", output.Error, StringComparison.Ordinal);
        Assert.Contains("Orkeon:Embeddings", output.Error, StringComparison.Ordinal);
        Assert.Empty(prompts);
    }

    [Fact]
    public async Task A_second_run_reads_in_its_prompt_what_the_first_run_produced()
    {
        var prompts = new List<string>();
        var embedder = new MockEmbeddingProvider();
        embedder.SetEmbeddingFunc(BagOfWords);
        await using var container = Host(
            Vendor(prompts, "Week 39 AI news: chips, regulation and open models.", "Week 40 AI news: agents everywhere."),
            embedder,
            // Bag-of-words vectors have their own scale; the section is the host's to set.
            new Dictionary<string, string?> { ["Orkeon:CrewMemory:MinScore"] = "0.3" });

        var first = await RunAsync(container, RememberingCrew);
        var second = await RunAsync(container, RememberingCrew);

        Assert.True(first.Succeeded, first.Error);
        Assert.True(second.Succeeded, second.Error);
        Assert.DoesNotContain(PromptDefaults.MemoriesHeader, prompts[0], StringComparison.Ordinal);
        Assert.Contains(PromptDefaults.MemoriesHeader, prompts[^1], StringComparison.Ordinal);
        Assert.Contains("Week 39 AI news: chips, regulation and open models.", prompts[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_crew_without_memory_neither_stores_nor_recalls_nor_needs_an_embedder()
    {
        var prompts = new List<string>();
        await using var container = Host(Vendor(prompts, "Week 39 AI news: chips, regulation and open models."), embedder: null);
        var forgetful = RememberingCrew.Replace("memory: true\n", string.Empty, StringComparison.Ordinal);

        var first = await RunAsync(container, forgetful);
        var second = await RunAsync(container, forgetful);

        Assert.True(first.Succeeded, first.Error);
        Assert.True(second.Succeeded, second.Error);
        Assert.DoesNotContain(PromptDefaults.MemoriesHeader, prompts[^1], StringComparison.Ordinal);
        var store = container.GetRequiredService<Orkeon.Domain.Memory.IMemoryProvider>();
        Assert.Empty(await store.SearchAsync(string.Empty, 100, cancellationToken: Ct));
    }

    [Fact]
    public void The_crew_memory_section_sets_the_bounds_of_a_recall()
    {
        using var container = Host(Vendor([], "never asked"), embedder: null, new Dictionary<string, string?>
        {
            ["Orkeon:CrewMemory:RecallLimit"] = "2",
            ["Orkeon:CrewMemory:MinScore"] = "0.75",
            ["Orkeon:CrewMemory:MaxChars"] = "1200",
        });

        var options = container.GetRequiredService<Microsoft.Extensions.Options.IOptions<Orkeon.Application.Memory.CrewMemoryOptions>>().Value;

        Assert.Equal(2, options.RecallLimit);
        Assert.Equal(0.75f, options.MinScore);
        Assert.Equal(1200, options.MaxChars);
    }

    [Fact]
    public async Task A_memory_provider_without_memory_is_refused_at_load_with_the_remedy()
    {
        await using var container = Host(Vendor([], "never asked"), embedder: null);
        var providerWithoutMemory = RememberingCrew.Replace("memory: true\n", "memoryProvider: sqlite\n", StringComparison.Ordinal);

        var error = await Assert.ThrowsAnyAsync<Exception>(() => RunAsync(container, providerWithoutMemory));

        Assert.Contains("memory: true", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-07")]
    public async Task ShouldNestRecallAndStoreUnderTask_WhenCrewHasMemory()
    {
        var embedder = new MockEmbeddingProvider();
        embedder.SetEmbeddingFunc(BagOfWords);
        await using var container = Host(Vendor([], "Week 39 AI news."), embedder);
        using var recorder = new ActivityRecorder(OrkeonActivitySources.AllNames);
        using var root = new Activity("ch07-root");
        root.Start();

        var output = await RunAsync(container, RememberingCrew);
        root.Stop();

        Assert.True(output.Succeeded, output.Error);
        var spans = recorder.Snapshot(root.TraceId);
        var task = Assert.Single(spans, a => a.OperationName == "task.run");
        var taskId = task.GetTagItem("orkeon.task.id");
        Assert.NotNull(taskId);
        foreach (var name in new[] { "memory.recall", "memory.store" })
        {
            var span = Assert.Single(spans, a => a.OperationName == name);
            Assert.Equal(task.SpanId, span.ParentSpanId);
            Assert.NotNull(span.GetTagItem("orkeon.memory.provider"));
            Assert.NotNull(span.GetTagItem("orkeon.memory.item_count"));
            Assert.NotNull(span.GetTagItem("orkeon.memory.operation"));
        }
    }

    [Fact]
    [Trait("BR", "CH-07")]
    public async Task ShouldMarkStoreInErrorUnderCompletedTask_WhenStoreFails()
    {
        var embedder = new MockEmbeddingProvider();
        embedder.SetEmbeddingFunc(BagOfWords);
        var memory = new MockMemoryProvider();
        memory.SetStoreException(new InvalidOperationException("store is down"));
        await using var container = Host(Vendor([], "Week 39 AI news."), embedder, memory: memory);
        using var recorder = new ActivityRecorder(OrkeonActivitySources.AllNames);
        using var root = new Activity("ch07-root-fail");
        root.Start();

        var output = await RunAsync(container, RememberingCrew);
        root.Stop();

        Assert.True(output.Succeeded, output.Error);
        var spans = recorder.Snapshot(root.TraceId);
        var task = Assert.Single(spans, a => a.OperationName == "task.run");
        var store = Assert.Single(spans, a => a.OperationName == "memory.store");
        Assert.Equal(task.SpanId, store.ParentSpanId);
        Assert.Equal(ActivityStatusCode.Error, store.Status);
        Assert.NotEqual(ActivityStatusCode.Error, task.Status);
    }

    [Fact]
    [Trait("BR", "CH-07")]
    public async Task ShouldMarkRecallInErrorUnderTask_WhenRecallFails()
    {
        var embedder = new MockEmbeddingProvider();
        var calls = 0;
        // The load-time probe embeds once and passes; the embedder then goes down, so the task's recall fails.
        embedder.SetEmbeddingFunc(text => Interlocked.Increment(ref calls) == 1
            ? BagOfWords(text)
            : throw new InvalidOperationException("embedder is down"));
        await using var container = Host(Vendor([], "Week 39 AI news."), embedder);
        using var recorder = new ActivityRecorder(OrkeonActivitySources.AllNames);
        using var root = new Activity("ch07-root-recall-fail");
        root.Start();

        var output = await RunAsync(container, RememberingCrew);
        root.Stop();

        Assert.True(output.Succeeded, output.Error);
        var spans = recorder.Snapshot(root.TraceId);
        var task = Assert.Single(spans, a => a.OperationName == "task.run");
        var recall = Assert.Single(spans, a => a.OperationName == "memory.recall");
        Assert.Equal(task.SpanId, recall.ParentSpanId);
        Assert.Equal(ActivityStatusCode.Error, recall.Status);
        Assert.False(string.IsNullOrEmpty(recall.GetTagItem("error.type") as string));
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public async Task ShouldNestEmbeddingsUnderMemorySpans_WhenCrewHasMemory()
    {
        var embedder = new MockEmbeddingProvider();
        embedder.SetEmbeddingFunc(BagOfWords);
        await using var container = Host(Vendor([], "Week 39 AI news."), embedder);
        using var recorder = new ActivityRecorder(OrkeonActivitySources.AllNames);
        using var root = new Activity("ch08-root");
        root.Start();

        var output = await RunAsync(container, RememberingCrew);
        root.Stop();

        Assert.True(output.Succeeded, output.Error);
        var spans = recorder.Snapshot(root.TraceId);
        var memorySpanIds = spans.Where(a => a.OperationName.StartsWith("memory.", StringComparison.Ordinal)).Select(a => a.SpanId).ToHashSet();
        var embeddings = spans.Where(a => a.OperationName.StartsWith("embeddings ", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(embeddings);
        // The readiness probe embeds under crew.run, outside any memory span; it is still never a root.
        Assert.All(embeddings, a => Assert.Contains(spans, p => p.SpanId == a.ParentSpanId));
        Assert.Contains(embeddings, a => memorySpanIds.Contains(a.ParentSpanId));
        foreach (var memorySpan in spans.Where(a => a.OperationName is "memory.recall" or "memory.store"))
            Assert.Contains(embeddings, a => a.ParentSpanId == memorySpan.SpanId);
    }

    private static float[] BagOfWords(string text)
    {
        var vector = new float[256];
        foreach (var word in text.ToLowerInvariant().Split([' ', '.', ',', ':', '\n', '\r', '-'], StringSplitOptions.RemoveEmptyEntries))
        {
            var slot = 0;
            foreach (var c in word)
                slot = (slot * 31 + c) & 0xFF;
            vector[slot] += 1f;
        }

        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        return norm > 0 ? [.. vector.Select(v => v / norm)] : vector;
    }
}
