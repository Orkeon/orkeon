using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Context;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.E2E.Tests;

/// <summary>
/// E2E tests for parallel task execution and rate limiting. The parallel test uses a real LLM
/// provider and skips gracefully when none is configured; the rate-limiting one runs offline.
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

        var config = LlmConfig.OnProfile() with { MaxTokens = 64, Temperature = 0.0 };

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

    /// <summary>
    /// GAP-38: an agent's <c>maxRpm</c> is applied — it used to be read by nothing, and this test
    /// read the default, then called the provider directly. Two turns of an agent with
    /// <c>MaxRpm(1)</c>, through the full container on a manual clock: the second waits until the
    /// first has left the minute. Offline: the model is scripted, nothing waits in real time.
    /// </summary>
    [Fact]
    public async Task RateLimiting_RespectsMaxRpm()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new ManualTimeProvider();
        var model = new ClockedModel(clock);
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddSingleton<TimeProvider>(clock);
        services.AddOrkeonLlmProvider(_ => model);
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        await using var container = services.BuildServiceProvider();
        await using var scope = container.CreateAsyncScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IExecutionOrchestrator>();
        var agent = new AgentBuilder().Role("Constrained Agent").Goal("Respond concisely").MaxRpm(1).Build();
        var context = new SimpleExecutionContext(CrewId.Create(), [], NullMemoryScope.Instance, []);

        var first = await orchestrator.ExecuteTaskCoreAsync(agent, CreateTestTask("Say 'hello' in one word."), context, ct);
        var second = orchestrator.ExecuteTaskCoreAsync(agent, CreateTestTask("Say 'world' in one word."), context, ct);
        await Polling.WaitUntilAsync(() => clock.PendingTimers > 0);
        Assert.Equal([TimeSpan.Zero], model.Calls);

        clock.Advance(TimeSpan.FromSeconds(60));
        var secondResult = await second;

        Assert.True(first.Success, first.Error);
        Assert.True(secondResult.Success, secondResult.Error);
        Assert.Equal([TimeSpan.Zero, TimeSpan.FromSeconds(60)], model.Calls);
    }

    /// <summary>A model that answers every call at once and records when, on the test's clock.</summary>
    private sealed class ClockedModel(ManualTimeProvider clock) : ILlmProvider
    {
        private readonly Lock _gate = new();
        private readonly List<TimeSpan> _calls = [];

        public string Name => "clocked";

        public IReadOnlyList<TimeSpan> Calls
        {
            get { lock (_gate) return [.. _calls]; }
        }

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
            Answer();

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
            Answer();

        private Task<LlmResponse> Answer()
        {
            lock (_gate)
                _calls.Add(clock.Elapsed);
            return Task.FromResult(new LlmResponse { Content = "done", PromptTokens = 5, CompletionTokens = 1, TokensUsed = 6 });
        }
    }
}
