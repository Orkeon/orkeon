using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Context;
using Orkeon.Application.Crew;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Tests.Shared.FileSystem;
using System.Reflection;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>
/// CH-06 - every field of a task's <see cref="LlmConfigOverride"/> reaches the provider as written on
/// both execution loops, the task's value winning over the agent's.
/// </summary>
public sealed class TaskLlmOverrideParityTests
{
    private sealed record Sample(object TaskValue, object AgentValue);

    // Profile is excluded: ExecutionOrchestrator.LlmFor chooses the client from it before the run, and the
    // merged Profile never goes on the ChatOptions.
    private const string ExcludedProperty = nameof(LlmConfigOverride.Profile);

    private static readonly Dictionary<string, Sample> Samples = new()
    {
        [nameof(LlmConfigOverride.ResponseFormat)] = new(LlmResponseFormat.JsonObject(), LlmResponseFormat.Text()),
        [nameof(LlmConfigOverride.Temperature)] = new(0.7, 0.2),
        [nameof(LlmConfigOverride.MaxTokens)] = new(128, 256),
        [nameof(LlmConfigOverride.TopP)] = new(0.95, 0.5),
        [nameof(LlmConfigOverride.Thinking)] = new(
            new LlmThinkingConfig { Effort = "low" }, new LlmThinkingConfig { Effort = "high" }),
        [nameof(LlmConfigOverride.Cache)] = new(
            new LlmCacheConfig { CacheSystemPrompt = true, Ttl = "1h" },
            new LlmCacheConfig { CacheSystemPrompt = true, Ttl = "5m" }),
    };

    public static TheoryData<string> OverrideProperties()
    {
        var data = new TheoryData<string>();
        foreach (var property in typeof(LlmConfigOverride).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (property.Name != ExcludedProperty)
                data.Add(property.Name);
        return data;
    }

    private static void AssertEveryPropertyHasASample(IReadOnlyDictionary<string, Sample> table)
    {
        foreach (var property in typeof(LlmConfigOverride).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.Name != ExcludedProperty && !table.ContainsKey(property.Name))
                throw new InvalidOperationException(
                    $"LlmConfigOverride.{property.Name} has no sample in the table: add one so the parity test covers it.");
        }
    }

    [Theory]
    [Trait("BR", "CH-06")]
    [MemberData(nameof(OverrideProperties))]
    public async System.Threading.Tasks.Task ShouldReachTheProviderAsWritten_OnBothLoops_ForEachOverrideProperty(string property)
    {
        AssertEveryPropertyHasASample(Samples);
        var sample = Samples[property];
        var task = new Orkeon.Domain.Task.CrewTaskBuilder()
            .Description("Do it").ExpectedOutput("x")
            .WithLlmOverride(Set(new LlmConfigOverride(), property, sample.TaskValue)).Build();

        foreach (var agentHasConfig in new[] { false, true })
        {
            var agentBuilder = new AgentBuilder().Role("Worker").Goal("Work");
            if (agentHasConfig)
                agentBuilder = agentBuilder.WithLlmConfig(Set(LlmConfig.Create("m"), property, sample.AgentValue));
            var agent = agentBuilder.Build();

            var full = new ScriptedFullLlmProvider();
            using var client = new LlmProviderToChatClientAdapter(full);
            var chatLoop = new ExecutionOrchestrator(
                NullLogger<ExecutionOrchestrator>.Instance, new LlmProviderAdapter(full),
                client, [], new FakeFileSystemService());
            await RunAsync(chatLoop, agent, task);
            Assert.NotEmpty(full.ReceivedConfigs);
            Assert.Equal(sample.TaskValue, Get(full.ReceivedConfigs[0]!, property));

            var basic = new ScriptedBasicLlmProvider();
            var fallbackLoop = new ExecutionOrchestrator(NullLogger<ExecutionOrchestrator>.Instance, basic);
            await RunAsync(fallbackLoop, agent, task);
            Assert.NotEmpty(basic.ReceivedConfigs);
            Assert.Equal(sample.TaskValue, Get(basic.ReceivedConfigs[0]!, property));
        }
    }

    [Fact]
    [Trait("BR", "CH-06")]
    public void ShouldFailNamingTheProperty_WhenAnOverridePropertyHasNoSample()
    {
        var incomplete = Samples.Where(s => s.Key != nameof(LlmConfigOverride.TopP))
            .ToDictionary(s => s.Key, s => s.Value);

        var error = Assert.Throws<InvalidOperationException>(() => AssertEveryPropertyHasASample(incomplete));

        Assert.Contains(nameof(LlmConfigOverride.TopP), error.Message, StringComparison.Ordinal);
    }

    private static T Set<T>(T target, string property, object value) where T : class
    {
        typeof(T).GetProperty(property)!.SetValue(target, value);
        return target;
    }

    private static object? Get(object source, string property) => source.GetType().GetProperty(property)!.GetValue(source);

    private static async System.Threading.Tasks.Task RunAsync(
        ExecutionOrchestrator orchestrator, Orkeon.Domain.Agent.Agent agent, Orkeon.Domain.Task.CrewTask task) =>
        await orchestrator.ExecuteTaskCoreAsync(
            agent, task, new SimpleExecutionContext(CrewId.Create(), [], NullMemoryScope.Instance, []),
            TestContext.Current.CancellationToken);
}
