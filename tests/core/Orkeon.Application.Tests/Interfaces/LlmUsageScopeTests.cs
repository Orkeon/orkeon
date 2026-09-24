using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Application.Tests.Interfaces;

/// <summary>
/// The ambient attribution every metered LLM call is stamped with (STUDIO-42 D-02): who the
/// call is for travels with the async flow, a scope says what it knows and inherits the
/// rest, and a call outside any scope is still somebody's — the unattributed bucket's.
/// </summary>
public sealed class LlmUsageScopeTests
{
    [Fact]
    public void A_call_outside_any_scope_is_unattributed()
    {
        var current = LlmUsageScope.Current;

        Assert.Equal(LlmUsageOperations.Unattributed, current.Operation);
        Assert.Empty(current.CrewId);
        Assert.Empty(current.AgentId);
        Assert.Empty(current.TaskId);
    }

    [Fact]
    public void A_scope_says_what_it_knows_and_inherits_the_rest()
    {
        using var crew = LlmUsageScope.Begin(crewId: "crew-1");
        using var agent = LlmUsageScope.Begin(LlmUsageOperations.Agent, agentId: "Writer", taskId: "task-1");
        using var rag = LlmUsageScope.Begin(LlmUsageOperations.Rag);

        var current = LlmUsageScope.Current;
        Assert.Equal(LlmUsageOperations.Rag, current.Operation);
        Assert.Equal("crew-1", current.CrewId);
        Assert.Equal("Writer", current.AgentId);
        Assert.Equal("task-1", current.TaskId);
    }

    [Fact]
    public void An_empty_name_never_erases_what_the_enclosing_scope_knew()
    {
        using var agent = LlmUsageScope.Begin(LlmUsageOperations.Agent, crewId: "crew-1", agentId: "Writer");
        using var script = LlmUsageScope.Begin("complete", crewId: string.Empty, agentId: string.Empty);

        Assert.Equal("crew-1", LlmUsageScope.Current.CrewId);
        Assert.Equal("Writer", LlmUsageScope.Current.AgentId);
        Assert.Equal("complete", LlmUsageScope.Current.Operation);
    }

    [Fact]
    public void Closing_a_scope_restores_the_one_it_was_opened_in()
    {
        using (LlmUsageScope.Begin(LlmUsageOperations.Agent, agentId: "Writer"))
        {
            using (LlmUsageScope.Begin(LlmUsageOperations.Memory))
                Assert.Equal(LlmUsageOperations.Memory, LlmUsageScope.Current.Operation);

            Assert.Equal(LlmUsageOperations.Agent, LlmUsageScope.Current.Operation);
            Assert.Equal("Writer", LlmUsageScope.Current.AgentId);
        }

        Assert.Same(LlmUsageAttribution.Unattributed, LlmUsageScope.Current);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_scope_opened_in_an_async_method_never_leaks_to_its_caller()
    {
        await WorkAsAnAgentAsync();

        Assert.Same(LlmUsageAttribution.Unattributed, LlmUsageScope.Current);

        static async System.Threading.Tasks.Task WorkAsAnAgentAsync()
        {
            using var scope = LlmUsageScope.Begin(LlmUsageOperations.Agent, agentId: "Writer");
            await System.Threading.Tasks.Task.Yield();
            Assert.Equal("Writer", LlmUsageScope.Current.AgentId);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task Concurrent_flows_keep_their_own_attribution()
    {
        // The parallel strategy runs several agents at once: each flow must keep its own.
        using var gate = new SemaphoreSlim(0, 2);
        var seen = await System.Threading.Tasks.Task.WhenAll(AgentAsync("Writer"), AgentAsync("Reviewer"), OpenGateAsync());

        Assert.Equal("Writer", seen[0]);
        Assert.Equal("Reviewer", seen[1]);

        async System.Threading.Tasks.Task<string> AgentAsync(string role)
        {
            using var scope = LlmUsageScope.Begin(LlmUsageOperations.Agent, agentId: role);
            await gate.WaitAsync(TestContext.Current.CancellationToken);
            return LlmUsageScope.Current.AgentId;
        }

        async System.Threading.Tasks.Task<string> OpenGateAsync()
        {
            await System.Threading.Tasks.Task.Yield();
            gate.Release(2);
            return string.Empty;
        }
    }
}
