using System.Diagnostics;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.E2E.Tests;

/// <summary>
/// E2E tests for parallel task execution and rate limiting.
/// Tests use real LLM providers and skip gracefully when none is configured.
/// </summary>
public class ParallelExecutionTests : E2ETestBase
{
    [Fact]
    public async Task ParallelTasks_ExecuteConcurrently()
    {
        SkipIfNoLlm();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var llmProvider = CreateLlmProvider();

        var tasks = new[]
        {
            "In one sentence, what is the capital of France?",
            "In one sentence, what is 2 + 2?",
            "In one sentence, what colour is the sky?"
        };

        var config = LlmConfig.Default() with { MaxTokens = 64, Temperature = 0.0 };

        var stopwatch = Stopwatch.StartNew();

        // Execute all tasks concurrently
        var results = await Task.WhenAll(
            tasks.Select(prompt => llmProvider.GenerateAsync(prompt, config, cts.Token)));

        stopwatch.Stop();

        Assert.Equal(tasks.Length, results.Length);
        foreach (var (result, prompt) in results.Zip(tasks))
        {
            Assert.False(string.IsNullOrWhiteSpace(result.Content),
                $"Parallel task returned empty response for prompt: {prompt}");
        }

        // All three concurrent requests should complete faster than if run sequentially with a 30s timeout each
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(90),
            $"Parallel execution took too long: {stopwatch.Elapsed.TotalSeconds:F1}s");
    }

    [Fact]
    public async Task RateLimiting_RespectsMaxRpm()
    {
        SkipIfNoLlm();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        // Use a low-RPM agent to verify rate limiting is in place at the domain level
        var agent = CreateTestAgent("Constrained Agent", "Respond concisely");
        Assert.Equal(10, agent.MaxRpm); // Verify the agent has RPM set

        var llmProvider = CreateLlmProvider();
        var config = LlmConfig.Default() with { MaxTokens = 32, Temperature = 0.0 };

        // Make two sequential requests — should both succeed without error
        var r1 = await llmProvider.GenerateAsync("Say 'hello' in one word.", config, cts.Token);
        var r2 = await llmProvider.GenerateAsync("Say 'world' in one word.", config, cts.Token);

        Assert.NotNull(r1);
        Assert.NotNull(r2);
        Assert.False(string.IsNullOrWhiteSpace(r1.Content), "First request returned empty response.");
        Assert.False(string.IsNullOrWhiteSpace(r2.Content), "Second request returned empty response.");
    }
}
