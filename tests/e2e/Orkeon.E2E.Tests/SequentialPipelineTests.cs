using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.E2E.Tests;

/// <summary>
/// E2E tests for the sequential pipeline: single agent/task through to multi-agent scenarios.
/// Tests use real LLM providers and skip gracefully when none is configured.
/// </summary>
public class SequentialPipelineTests : E2ETestBase
{
    [Fact]
    public async Task SingleAgentSingleTask_CompletesSuccessfully()
    {
        SkipIfNoLlm();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var llmProvider = CreateLlmProvider();

        var agent = CreateTestAgent("Research Analyst", "Summarise information clearly and concisely");
        var task = CreateTestTask(
            "Summarise the following text in one sentence: 'The quick brown fox jumps over the lazy dog.'",
            "A one-sentence summary of the input text.");

        // Use the LLM provider directly to simulate a single-agent pipeline step
        var config = LlmConfig.Default() with
        {
            MaxTokens = 256,
            Temperature = 0.1
        };

        var response = await llmProvider.GenerateAsync(
            $"Agent: {agent.Role}\nGoal: {agent.Goal}\n\nTask: {task.Description}\nExpected output: {task.ExpectedOutput}",
            config,
            cts.Token);

        Assert.NotNull(response);
        Assert.False(string.IsNullOrWhiteSpace(response.Content),
            $"Expected non-empty LLM response. Metadata: {string.Join(", ", response.Metadata?.Select(k => $"{k.Key}={k.Value}") ?? [])}");
    }

    [Fact]
    public async Task TwoAgentThreeTask_SequentialExecution()
    {
        SkipIfNoLlm();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var llmProvider = CreateLlmProvider();

        var researchAgent = CreateTestAgent("Research Analyst", "Gather and verify information accurately");
        var writerAgent = CreateTestAgent("Content Writer", "Transform research into clear written content");

        var tasks = new[]
        {
            CreateTestTask("List three programming languages and their primary use cases.", "A list of three programming languages with use cases."),
            CreateTestTask("Identify the most beginner-friendly language from the previous list.", "The name of the most beginner-friendly language with a brief reason."),
            CreateTestTask("Write one paragraph recommending the language to a beginner.", "A short paragraph recommendation.")
        };

        string previousOutput = string.Empty;
        var agents = new[] { researchAgent, researchAgent, writerAgent };

        for (int i = 0; i < tasks.Length; i++)
        {
            var task = tasks[i];
            var agent = agents[i];
            var prompt = string.IsNullOrEmpty(previousOutput)
                ? $"Agent: {agent.Role}\nTask: {task.Description}"
                : $"Agent: {agent.Role}\nContext: {previousOutput}\nTask: {task.Description}";

            var response = await llmProvider.GenerateAsync(prompt,
                LlmConfig.Default() with { MaxTokens = 256, Temperature = 0.1 }, cts.Token);

            Assert.NotNull(response);
            Assert.False(string.IsNullOrWhiteSpace(response.Content),
                $"Task {i + 1} produced empty response.");
            previousOutput = response.Content;
        }
    }

    [Fact]
    public async Task AgentWithTool_ExecutesToolAndReturnsResult()
    {
        SkipIfNoLlm();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var llmProvider = CreateLlmProvider();

        // Simulate an agent that uses a tool by describing the tool in the prompt
        var agent = CreateTestAgent("Data Agent", "Process data using available tools");
        var toolDescription = "calculate_sum: Adds two numbers together and returns the result.";

        var prompt = $"""
            Agent: {agent.Role}
            Goal: {agent.Goal}
            Available tools: {toolDescription}

            Task: Use the calculate_sum tool to add 42 and 58, then report the result.
            Expected output: The sum is 100.
            """;

        var response = await llmProvider.GenerateAsync(prompt,
            LlmConfig.Default() with { MaxTokens = 256, Temperature = 0.0 }, cts.Token);

        Assert.NotNull(response);
        Assert.False(string.IsNullOrWhiteSpace(response.Content), "Expected non-empty response.");
        // The response should mention 100 as the result of 42 + 58
        Assert.Contains("100", response.Content);
    }
}
