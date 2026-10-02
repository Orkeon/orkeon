using System.Net;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Summarizers;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Infrastructure.Memory.Cognitive;
using Orkeon.Infrastructure.Parsing;
using Polly;
using TestHttpClientFactory = Orkeon.Infrastructure.Tests.TestDoubles.TestHttpClientFactory;
using TestHttpMessageHandler = Orkeon.Infrastructure.Tests.TestDoubles.TestHttpMessageHandler;

namespace Orkeon.Infrastructure.Tests.LLMs.CallConfiguration;

/// <summary>
/// GAP-29: the callers that build a configuration of their own reach a real provider with its
/// key, its URL and its timeout. Each of them failed on a keyed vendor — and none said so: the
/// planner reported a plan "missing" its tasks, the cognitive memory fell back to a default
/// analysis, the context window truncated, the RaggableTree summarizer returned nothing, and the
/// chat client registered without a base configuration threw "API key is required".
/// </summary>
/// <remarks>The key, the URL and the timeout are configured on the provider only.</remarks>
public sealed class CallersOnTheProvidersConnectionTests
{
    private const string Key = "sk-provider-only";
    private const int TimeoutSeconds = 600;
    private static readonly Uri Endpoint = new("https://llm.example.test/v1");
    private static readonly Uri ChatCompletions = new("https://llm.example.test/v1/chat/completions");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Answer(string content) => JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { role = "assistant", content } } },
        usage = new { total_tokens = 2, prompt_tokens = 1, completion_tokens = 1 },
    });

    private sealed class Vendor : IDisposable
    {
        private readonly TestHttpMessageHandler _handler;
        private readonly HttpClient _client;

        public Vendor(string content, LlmConfig? configured = null)
        {
            _handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, Answer(content));
            _client = new HttpClient(_handler);
            var factory = new TestHttpClientFactory();
            factory.RegisterClient(nameof(DeepSeekLlmProvider), _client);
            Provider = new DeepSeekLlmProvider(
                configured ?? LlmConfig.Create("deepseek-chat", Key) with { BaseUrl = Endpoint, TimeoutSeconds = TimeoutSeconds },
                factory,
                Policy.NoOpAsync<HttpResponseMessage>());
        }

        public DeepSeekLlmProvider Provider { get; }

        public HttpRequestMessage Sent => Assert.Single(_handler.CapturedRequests);

        public async Task<JsonElement> SentPayloadAsync()
        {
            using var document = JsonDocument.Parse(await Sent.Content!.ReadAsStringAsync(Ct));
            return document.RootElement.Clone();
        }

        public void AssertTheProvidersConnection()
        {
            Assert.NotNull(Sent.Headers.Authorization);
            Assert.Equal($"Bearer {Key}", Sent.Headers.Authorization!.ToString());
            Assert.Equal(ChatCompletions, Sent.RequestUri);
            Assert.Equal(TimeSpan.FromSeconds(TimeoutSeconds), _client.Timeout);
        }

        public void Dispose()
        {
            Provider.Dispose();
            _client.Dispose();
            _handler.Dispose();
        }
    }

    [Fact]
    public async Task The_planner_plans_on_the_providers_connection()
    {
        var taskId = TaskId.Create();
        var plan = JsonSerializer.Serialize(new { tasks = new[] { new { task = taskId.ToString(), order = 1 } } });
        using var vendor = new Vendor(plan);
        var crew = Orkeon.Domain.Crew.Crew.Create("Plan the work", ProcessType.Sequential, false, false);

        var created = await CrewPlanner.Create(vendor.Provider, new ExecutionPlanParser()).CreatePlanAsync(
            new PlanningContext(crew.Id, crew.Goal, crew.Agents), [taskId], new CrewInput("context"));

        vendor.AssertTheProvidersConnection();
        Assert.Equal(taskId, Assert.Single(created.Tasks).TaskId);
        Assert.Equal(0.3, (await vendor.SentPayloadAsync()).GetProperty("temperature").GetDouble(), precision: 3);
    }

    [Fact]
    public async Task A_cognitive_memory_analysis_runs_on_the_providers_connection()
    {
        using var vendor = new Vendor(
            """{"importance":0.9,"category":"decision","key_entities":["Orkeon"],"summary":"A decision.","suggested_tags":["adr"],"reasoning":"It decides."}""");
        var analyzer = new MemoryAnalyzer(
            vendor.Provider, Options.Create(new CognitiveMemoryOptions()), NullLogger<MemoryAnalyzer>.Instance);

        var analysis = await analyzer.AnalyzeAsync("We keep the planner on the default profile.", null, Ct);

        vendor.AssertTheProvidersConnection();
        // The model's analysis, not the fallback a failed call produces.
        Assert.Equal("decision", analysis.Category);
    }

    [Fact]
    public async Task The_context_window_summary_runs_on_the_providers_connection()
    {
        using var vendor = new Vendor("A short summary.");
        var manager = new OpenAIContextWindowManager(
            new SimpleTokenCounter(), vendor.Provider, NullLogger<OpenAIContextWindowManager>.Instance);

        var summary = await manager.SummarizeIfNeededAsync(string.Join(' ', Enumerable.Repeat("word", 400)), maxTokens: 50);

        vendor.AssertTheProvidersConnection();
        Assert.Equal("A short summary.", summary);
    }

    [Fact]
    public async Task The_RaggableTree_summarizer_runs_on_the_providers_connection_and_model()
    {
        using var vendor = new Vendor("Manages the users.");
        var summarizer = new LlmNodeSummarizer(vendor.Provider);
        var node = new RaggableNode
        {
            Id = "id::UserService",
            Kind = UniversalNodeKind.Class,
            Name = "UserService",
            VirtualFilePath = "/workspace/UserService.cs",
            Range = new NodeRange(0, 0, 0, 0, 0),
            Level = NodeLevel.L3_Symbol,
            Language = "csharp",
            SourceSnippet = new string('x', 200),
            Sha256 = string.Empty,
            Fqn = "App.UserService",
        };

        var summary = await summarizer.SummarizeAsync(node, new SummarizationContext(null, [], [], null), Ct);

        vendor.AssertTheProvidersConnection();
        Assert.Equal("Manages the users.", summary);
        // No vendor's model imposed: the summarizer names none, and the provider runs its own.
        Assert.Equal("deepseek-chat", (await vendor.SentPayloadAsync()).GetProperty("model").GetString());
    }

    [Fact]
    public async Task A_chat_client_registered_without_a_base_configuration_runs_on_the_providers_connection()
    {
        // AddOrkeonLlmProvider(provider) without a base configuration; the RAG pipelines and the
        // judges override the temperature, which built a configuration from scratch.
        using var vendor = new Vendor("ok");
        using var chat = new LlmProviderToChatClientAdapter(vendor.Provider);

        var response = await chat.GetResponseAsync("hello", new ChatOptions { Temperature = 0f }, Ct);

        vendor.AssertTheProvidersConnection();
        Assert.Equal("ok", response.Text);
    }

    [Fact]
    public async Task A_chat_client_without_a_base_configuration_starts_from_the_providers_settings()
    {
        // An override of one option keeps the provider's other settings — its temperature here —
        // instead of the defaults of a blank configuration.
        using var vendor = new Vendor("ok", LlmConfig.Create("deepseek-chat", Key) with
        {
            BaseUrl = Endpoint,
            TimeoutSeconds = TimeoutSeconds,
            Temperature = 0.2,
        });
        using var chat = new LlmProviderToChatClientAdapter(MeteredLlmProvider.Wrap(vendor.Provider, sink: null));

        await chat.GetResponseAsync("hello", new ChatOptions { MaxOutputTokens = 300 }, Ct);

        var payload = await vendor.SentPayloadAsync();
        Assert.Equal(0.2, payload.GetProperty("temperature").GetDouble(), precision: 3);
        Assert.Equal(300, payload.GetProperty("max_tokens").GetInt32());
        vendor.AssertTheProvidersConnection();
    }
}
