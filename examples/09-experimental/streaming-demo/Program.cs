using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Context;
using Orkeon.Domain.Common;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Shared.ValueObjects;
using Orkeon.Application.DependencyInjection;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Task;

// ====================================================================
// Orkeon Streaming Demo
// Demonstrates real-time streaming of agent execution with IChatClient.
// ====================================================================

Console.WriteLine("=== Orkeon Streaming Demo ===\n");

// 1. Setup DI with infrastructure
var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .Build();

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(config);
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
services.AddHttpClient();

// Register LLM provider from appsettings.json (Docker Model Runner)
var llmSection = config.GetSection("Llm");
var llmConfig = new LlmConfig
{
    Model = llmSection["Model"] ?? "gpt-4",
    BaseUrl = llmSection["BaseUrl"],
    ApiKey = llmSection["ApiKey"],
    Temperature = double.TryParse(llmSection["Temperature"], out var temp) ? temp : 0.7,
    MaxTokens = int.TryParse(llmSection["MaxTokens"], out var mt) ? mt : 4096
};
services.AddSingleton<Orkeon.Domain.Shared.ILlmProvider>(sp =>
    new OpenAIProvider(llmConfig, sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<ILogger<OpenAIProvider>>()));
services.AddSingleton<IBasicLlmProvider>(sp =>
    new LlmProviderAdapter(sp.GetRequiredService<Orkeon.Domain.Shared.ILlmProvider>()));
services.AddSingleton<IChatClient>(sp =>
    new LlmProviderToChatClientAdapter(sp.GetRequiredService<Orkeon.Domain.Shared.ILlmProvider>()));

services.AddOrkeonApplication();
services.AddOrkeonInfrastructure();

var provider = services.BuildServiceProvider();

// 2. Create an agent and task
var agent = new AgentBuilder()
    .Role("Research Analyst")
    .Goal("Analyze market trends and provide insights")
    .Backstory("Senior analyst with 10 years experience in tech market research")
    .AllowDelegation(false)
    .Build();

var task = new CrewTaskBuilder()
    .Description("Analyze the current state of AI agent frameworks")
    .ExpectedOutput("A concise summary of the top 3 AI agent frameworks in 2026")
    .Build();

// 3. Create a simple execution context
var context = new SimpleExecutionContext(
    CrewId.Create(),
    new Dictionary<string, string>(),
    new NullMemoryScope(),
    new List<Orkeon.Application.Execution.TaskOutput>());

// 4. Stream execution
var streamingService = provider.GetRequiredService<IStreamingAgentExecutionService>();

Console.WriteLine($"Agent: {agent.Role}");
Console.WriteLine($"Task: {task.Description}\n");
Console.WriteLine("--- Streaming execution ---\n");

await foreach (var thought in streamingService.StreamExecutionAsync(agent, task, context))
{
    var prefix = thought.Type switch
    {
        AgentThought.ThoughtType.Reasoning => "[Thinking] ",
        AgentThought.ThoughtType.ToolSelection => "[Tool] ",
        AgentThought.ThoughtType.ToolExecution => "[Result] ",
        AgentThought.ThoughtType.Conclusion => "\n[Answer] ",
        AgentThought.ThoughtType.Error => "[ERROR] ",
        _ => ""
    };

    if (thought.Type == AgentThought.ThoughtType.Reasoning &&
        !thought.Content.StartsWith("Starting task"))
    {
        // Stream tokens inline
        Console.Write(thought.Content);
    }
    else
    {
        Console.WriteLine($"{prefix}{thought.Content}");
    }
}

Console.WriteLine("\n\n--- Done ---");

// ====================================================================
// Mock chat client that simulates streaming responses
// ====================================================================

class MockStreamingChatClient : IChatClient
{
    public ChatClientMetadata Metadata => new("MockStreaming");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ChatResponse(
            new ChatMessage(ChatRole.Assistant, "Mock response")));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var tokens = new[]
        {
            "Based on ", "my analysis, ", "the top 3 ", "AI agent ",
            "frameworks in 2026 ", "are:\n\n",
            "1. **Orkeon** - ", "Multi-agent orchestration ", "with role-based collaboration\n",
            "2. **AutoGen** - ", "Microsoft's framework ", "for conversational agents\n",
            "3. **LangGraph** - ", "Graph-based agent ", "workflow engine\n\n",
            "Each framework ", "excels at ", "different use cases."
        };

        foreach (var token in tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(50, cancellationToken); // Simulate latency
            yield return new ChatResponseUpdate(ChatRole.Assistant, token);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}

// Minimal memory scope for the demo
class NullMemoryScope : Orkeon.Application.Interfaces.Ports.IMemoryScope
{
    public string AgentId => "demo";
    public string ScopeId => "demo-scope";
    public Task<T> ExecuteInScopeAsync<T>(Func<Task<T>> operation) => operation();
    public Task ExecuteInScopeAsync(Func<Task> operation) => operation();
    public void Dispose() { }
}
