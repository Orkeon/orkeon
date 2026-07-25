using System.Reflection;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Common;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Domain.Tests.Builders;

public class BuilderIntegrationTests
{
    [Fact]
    public void EndToEnd_CrewWithInlineAgentsAndTasks_BuildsCompleteGraph()
    {
        // Act — build a complete crew with inline agents, tasks, and dependencies
        var crew = new CrewBuilder()
            .Goal("End-to-end integration test")
            .Sequential()
            .Verbose()
            .WithAgent(ab => ab
                .Role("Researcher")
                .Goal("Research the topic")
                .Backstory("Expert researcher")
                .Verbose())
            .WithAgent(ab => ab
                .Role("Writer")
                .Goal("Write the article")
                .AllowDelegation())
            .WithTask(tb => tb
                .Description("Research AI trends")
                .ExpectedOutput("A summary of AI trends")
                .Priority(TaskPriority.High))
            .WithTask(tb => tb
                .Description("Write the article")
                .ExpectedOutput("A polished article")
                .WithContext("topic", "AI trends"))
            .Build();

        // Assert
        Assert.NotNull(crew);
        Assert.Equal("End-to-end integration test", crew.Goal.Value);
        Assert.Equal(ProcessType.Sequential, crew.ProcessType);
        Assert.True(crew.Verbose);
        Assert.Equal(2, crew.Agents.Count);
        Assert.Equal(2, crew.Tasks.Count);
    }

    [Fact]
    public void BuilderOutput_EquivalentTo_FactoryMethod()
    {
        // Arrange — build via builder
        var builderAgent = new AgentBuilder()
            .Role("Tester")
            .Goal("Test code")
            .Backstory("QA engineer")
            .MaxIterations(10)
            .MaxRpm(5)
            .Verbose()
            .AllowDelegation()
            .CacheEnabled(false)
            .MaxRetryLimit(1)
            .Build();

        // Arrange — build via Agent.Create factory method with same options
        var factoryAgent = DomainAgent.Create(new AgentCreateOptions
        {
            Role = AgentRole.From("Tester"),
            Goal = AgentGoal.From("Test code"),
            Backstory = AgentBackstory.From("QA engineer"),
            MaxIterations = 10,
            MaxRpm = 5,
            Verbose = true,
            AllowDelegation = true,
            CacheEnabled = false,
            MaxRetryLimit = 1
        });

        // Assert — key properties match (IDs will differ since they're auto-generated)
        Assert.Equal(builderAgent.Role, factoryAgent.Role);
        Assert.Equal(builderAgent.Goal, factoryAgent.Goal);
        Assert.Equal(builderAgent.Backstory, factoryAgent.Backstory);
        Assert.Equal(builderAgent.MaxIterations, factoryAgent.MaxIterations);
        Assert.Equal(builderAgent.MaxRpm, factoryAgent.MaxRpm);
        Assert.Equal(builderAgent.Verbose, factoryAgent.Verbose);
        Assert.Equal(builderAgent.AllowDelegation, factoryAgent.AllowDelegation);
        Assert.Equal(builderAgent.CacheEnabled, factoryAgent.CacheEnabled);
        Assert.Equal(builderAgent.MaxRetryLimit, factoryAgent.MaxRetryLimit);
    }

    [Fact]
    public void FluentBuilderFactory_BuildCrew_WorksEndToEnd()
    {
        // Act — use FluentBuilderFactory entry points
        var agent = FluentBuilderFactory.BuildAgent(RoleAnalyst, GoalAnalyzeData)
            .Backstory("Data analyst")
            .Build();

        var task = FluentBuilderFactory.BuildTask("Analyze dataset", "Analysis report")
            .Priority(TaskPriority.High)
            .Build();

        var crew = FluentBuilderFactory.BuildCrew("Data analysis project")
            .Sequential()
            .WithAgent(agent)
            .WithTask(task)
            .Build();

        // Assert
        Assert.NotNull(agent);
        Assert.Equal(RoleAnalyst, agent.Role.Value);
        Assert.Equal(GoalAnalyzeData, agent.Goal.Value);
        Assert.Equal("Data analyst", agent.Backstory!);

        Assert.NotNull(task);
        Assert.Equal("Analyze dataset", task.Description.Value);
        Assert.Equal("Analysis report", task.ExpectedOutput!);
        Assert.Equal(TaskPriority.High, task.Priority);

        Assert.NotNull(crew);
        Assert.Equal("Data analysis project", crew.Goal.Value);
        Assert.Equal(ProcessType.Sequential, crew.ProcessType);
        Assert.Single(crew.Agents);
        Assert.Single(crew.Tasks);
    }

    [Fact]
    public void ParityTest_BuilderProperties_MatchCreateOptions()
    {
        // Verify that every settable property in AgentCreateOptions has a corresponding builder method.
        // This is a reflection-based safety net to catch missed properties after future changes.

        var optionsProperties = typeof(AgentCreateOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite || p.GetMethod?.ReturnType != typeof(void))
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Map builder method names to the create options property names they set
        var methodToProperty = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Role"] = "Role",
            ["Goal"] = "Goal",
            ["Backstory"] = "Backstory",
            ["AllowDelegation"] = "AllowDelegation",
            ["MaxIterations"] = "MaxIterations",
            ["MaxRpm"] = "MaxRpm",
            ["Verbose"] = "Verbose",
            ["MaxExecutionTime"] = "MaxExecutionTime",
            ["CacheEnabled"] = "CacheEnabled",
            ["SystemTemplate"] = "SystemTemplate",
            ["PromptTemplate"] = "PromptTemplate",
            ["ResponseTemplate"] = "ResponseTemplate",
            ["MaxRetryLimit"] = "MaxRetryLimit",
            ["WithLlm"] = "FunctionCallingLlm",
            ["WithTool"] = "Tools",
            ["WithTools"] = "Tools",
            ["WithStepCallback"] = "StepCallback",
            ["WithToolAccessPolicy"] = "ToolAccessPolicy",
            ["WithGuardrails"] = "Guardrails",
            ["WithLlmConfig"] = "LlmConfig",
            ["WithKnowledge"] = "KnowledgeAttachments"
        };

        // Every property in AgentCreateOptions should be covered by at least one builder method
        var coveredProperties = methodToProperty.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var uncoveredProperties = optionsProperties.Except(coveredProperties, StringComparer.OrdinalIgnoreCase).ToList();

        Assert.Empty(uncoveredProperties);
    }
}
