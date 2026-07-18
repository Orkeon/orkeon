using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Persistence.Crew;
using Orkeon.Infrastructure.Persistence.Task;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// Task-level guardrails (P2-O-04): YAML parse + mapper survival to <see cref="TaskConfiguration"/>,
/// carry onto the domain <see cref="CrewTask"/> via <see cref="CrewFactory"/>, and agent-level
/// guardrails left unchanged.
/// </summary>
public class TaskGuardrailsTests
{
    private static YamlCrewDefinitionLoader NewLoader()
        => new(new YamlDotNetSerializer(), new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);

    [Fact]
    public async Task LoadFromString_ShouldParseTaskGuardrails_IntoTaskConfiguration()
    {
        const string yaml = """
            name: guarded-crew
            goal: test
            process: sequential
            agents:
              worker:
                role: Worker
                goal: work
            tasks:
              do_it:
                description: Do it
                expected_output: Output
                agent: worker
                guardrails:
                  header: "TASK RULES:"
                  rules:
                    - cite sources
                  toolRules:
                    file_write:
                      - never overwrite
            """;

        var config = await NewLoader().LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        var task = Assert.Single(config.Tasks);
        Assert.NotNull(task.Guardrails);
        Assert.Equal("TASK RULES:", task.Guardrails!.Header);
        Assert.Contains("cite sources", task.Guardrails.Rules);
        Assert.True(task.Guardrails.ToolRules.ContainsKey("file_write"));
    }

    [Fact]
    public async Task LoadFromString_ShouldResolveTaskGuardrailPreset()
    {
        const string yaml = """
            name: guarded-crew
            goal: test
            process: sequential
            agents:
              worker:
                role: Worker
                goal: work
            tasks:
              do_it:
                description: Do it
                expected_output: Output
                agent: worker
                guardrails:
                  preset: strict
            """;

        var config = await NewLoader().LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        var task = Assert.Single(config.Tasks);
        Assert.NotNull(task.Guardrails);
        Assert.False(task.Guardrails!.IsEmpty); // the strict preset carries rules
    }

    [Fact]
    public async Task LoadFromString_ShouldLeaveAgentGuardrailsUnchanged()
    {
        const string yaml = """
            name: guarded-crew
            goal: test
            process: sequential
            agents:
              worker:
                role: Worker
                goal: work
                guardrails:
                  header: "AGENT RULES:"
                  rules:
                    - stay factual
            tasks:
              do_it:
                description: Do it
                expected_output: Output
                agent: worker
            """;

        var config = await NewLoader().LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        var agent = Assert.Single(config.Agents);
        Assert.NotNull(agent.Guardrails);
        Assert.Equal("AGENT RULES:", agent.Guardrails!.Header);
        Assert.Null(Assert.Single(config.Tasks).Guardrails);
    }

    [Fact]
    public async Task CreateFromConfig_ShouldCarryTaskGuardrails_OntoDomainCrewTask()
    {
        var unitOfWork = new NullUnitOfWork();
        var taskRepo = new InMemoryTaskRepository(unitOfWork);
        var loaderMock = new MockCrewDefinitionLoader();
        loaderMock.SetValidateResult(new CrewDefinitionValidationResult(true, Array.Empty<string>(), Array.Empty<string>()));
        var factory = new CrewFactory(
            loaderMock,
            new MockToolRegistry(),
            NullLogger<CrewFactory>.Instance,
            new InMemoryCrewRepository(unitOfWork),
            new InMemoryAgentRepository(unitOfWork),
            taskRepo);

        var agentId = AgentId.Create();
        var config = new CrewConfiguration
        {
            Name = "guarded-crew",
            Goal = "test",
            Process = ProcessType.Sequential,
            Agents = [new AgentConfiguration { Id = agentId, Role = "worker", Goal = "do work", Backstory = "b" }],
            Tasks =
            [
                new TaskConfiguration
                {
                    Id = TaskId.Create(),
                    Description = "do something",
                    ExpectedOutput = "output",
                    AssignedAgentId = agentId,
                    Guardrails = new GuardrailsConfig { Header = "TASK RULES:", Rules = ["cite sources"] }
                }
            ]
        };

        var crew = await factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken);

        var taskId = Assert.Single(crew.Tasks);
        var domainTask = await taskRepo.GetByIdAsync(taskId, TestContext.Current.CancellationToken);
        Assert.NotNull(domainTask);
        Assert.NotNull(domainTask!.Guardrails);
        Assert.Equal("TASK RULES:", domainTask.Guardrails!.Header);
        Assert.Contains("cite sources", domainTask.Guardrails.Rules);
    }
}
