using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Context;
using Orkeon.Domain.Common;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Application.DependencyInjection;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Adapters;
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
var llmConfig = LlmConfig.Create(llmSection["Model"] ?? "gpt-4") with
{
    BaseUrl = llmSection["BaseUrl"] is { } baseUrl ? new Uri(baseUrl) : null,
#pragma warning disable CS0618 // demo wires the key directly from appsettings
    ApiKey = llmSection["ApiKey"],
#pragma warning restore CS0618
    Temperature = double.TryParse(llmSection["Temperature"], out var temp) ? temp : 0.7,
    MaxTokens = int.TryParse(llmSection["MaxTokens"], out var mt) ? mt : 4096
};
services.AddSingleton<Orkeon.Domain.SharedKernel.ILlmProvider>(sp =>
    new OpenAIProvider(llmConfig, sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<ILogger<OpenAIProvider>>()));
services.AddSingleton<IBasicLlmProvider>(sp =>
    new LlmProviderAdapter(sp.GetRequiredService<Orkeon.Domain.SharedKernel.ILlmProvider>()));
services.AddSingleton<IChatClient>(sp =>
    new LlmProviderToChatClientAdapter(sp.GetRequiredService<Orkeon.Domain.SharedKernel.ILlmProvider>()));

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
    NullMemoryScope.Instance,
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
        !thought.Content.StartsWith("Starting task", StringComparison.Ordinal))
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
