using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;

namespace Orkeon.E2E.Tests;

/// <summary>
/// E2E tests for the memory integration: agent context retention and crew shared memory.
/// Memory store tests do NOT require an LLM provider.
/// LLM-backed memory tests skip gracefully when no provider is configured.
/// </summary>
public class MemoryIntegrationTests : E2ETestBase
{
    [Fact]
    public async Task AgentRemembersContext_AcrossTasks()
    {
        // Test uses InMemoryProvider directly — no LLM required.
        var memory = new InMemoryProvider(
            ServiceProvider.GetService<ILogger<InMemoryProvider>>());

        var agentId = "agent-researcher";

        // Store context from first task
        var firstTaskContext = MemoryItem.Create(
            content: "Paris is the capital of France. It has a population of approximately 2.1 million.",
            source: "task-0",
            tags: ["geography"],
            importance: 0.8f,
            customProperties: new Dictionary<string, string>
            {
                ["agent_id"] = agentId,
                ["task_index"] = "0"
            });

        await memory.StoreAsync($"{agentId}:task:0:context", firstTaskContext, TestContext.Current.CancellationToken);

        // Retrieve context for second task
        var retrieved = await memory.GetAsync($"{agentId}:task:0:context", TestContext.Current.CancellationToken);

        Assert.NotNull(retrieved);
        Assert.Contains("Paris", retrieved.Content);
        Assert.Equal(agentId, retrieved.Metadata.CustomProperties?["agent_id"]);

        // Simulate agent using context in second task
        var secondTaskContext = MemoryItem.Create(
            content: $"Based on prior research: {retrieved.Content} The Eiffel Tower is located in Paris.",
            source: TaskId1,
            tags: ["landmarks"],
            importance: 0.7f,
            customProperties: new Dictionary<string, string>
            {
                ["agent_id"] = agentId,
                ["task_index"] = "1"
            });

        await memory.StoreAsync($"{agentId}:task:1:context", secondTaskContext, TestContext.Current.CancellationToken);

        var secondRetrieved = await memory.GetAsync($"{agentId}:task:1:context", TestContext.Current.CancellationToken);

        Assert.NotNull(secondRetrieved);
        Assert.Contains("Eiffel Tower", secondRetrieved.Content);
        Assert.Contains("Paris", secondRetrieved.Content);
    }

    [Fact]
    public async Task CrewSharedMemory_AccessibleByAllAgents()
    {
        // Test uses InMemoryProvider directly — no LLM required.
        var sharedMemory = new InMemoryProvider(
            ServiceProvider.GetService<ILogger<InMemoryProvider>>());

        var crew = CreateTestCrew("Research and report on AI trends");
        var agent1 = CreateTestAgent("Research Analyst", "Gather information");
        var agent2 = CreateTestAgent("Report Writer", "Synthesise information into reports");
        var agent3 = CreateTestAgent("Fact Checker", "Verify accuracy of information");

        var crewId = crew.Id.ToString();

        // Agent 1 stores research findings in shared crew memory
        var researchFindings = MemoryItem.Create(
            content: "AI adoption grew by 38% in 2024. Large language models are now used by 67% of Fortune 500 companies.",
            source: "research",
            importance: 0.9f,
            customProperties: new Dictionary<string, string>
            {
                ["written_by"] = agent1.Id.ToString(),
                ["crew_id"] = crewId,
                ["type"] = "research"
            });

        await sharedMemory.StoreAsync($"crew:{crewId}:research:findings", researchFindings, TestContext.Current.CancellationToken);

        // Agent 2 reads from shared memory and adds a report section
        var findings = await sharedMemory.GetAsync($"crew:{crewId}:research:findings", TestContext.Current.CancellationToken);
        Assert.NotNull(findings);

        var reportSection = MemoryItem.Create(
            content: $"Executive Summary: {findings.Content}",
            source: "report",
            importance: 0.8f,
            customProperties: new Dictionary<string, string>
            {
                ["written_by"] = agent2.Id.ToString(),
                ["crew_id"] = crewId,
                ["type"] = "report_section"
            });

        await sharedMemory.StoreAsync($"crew:{crewId}:report:summary", reportSection, TestContext.Current.CancellationToken);

        // Agent 3 reads both to fact-check
        var factCheckFindings = await sharedMemory.GetAsync($"crew:{crewId}:research:findings", TestContext.Current.CancellationToken);
        var factCheckReport = await sharedMemory.GetAsync($"crew:{crewId}:report:summary", TestContext.Current.CancellationToken);

        Assert.NotNull(factCheckFindings);
        Assert.NotNull(factCheckReport);
        Assert.Contains("38%", factCheckFindings.Content);
        Assert.Contains("Executive Summary", factCheckReport.Content);

        // Verify all three agents can access the same shared crew memory
        Assert.Equal(crewId, factCheckFindings.Metadata.CustomProperties?["crew_id"]);
        Assert.Equal(crewId, factCheckReport.Metadata.CustomProperties?["crew_id"]);

        // Verify authorship metadata is preserved
        Assert.Equal(agent1.Id.ToString(), factCheckFindings.Metadata.CustomProperties?["written_by"]);
        Assert.Equal(agent2.Id.ToString(), factCheckReport.Metadata.CustomProperties?["written_by"]);
    }
}
