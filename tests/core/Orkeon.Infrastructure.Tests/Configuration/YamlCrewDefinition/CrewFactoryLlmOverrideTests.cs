using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces;
using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Persistence.Crew;
using Orkeon.Infrastructure.Persistence.Task;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// Regression: <see cref="CrewFactory.CreateTasks"/> must propagate
/// <see cref="TaskConfiguration.LlmOverride"/> onto the materialised
/// <see cref="CrewTask"/>. Without this, YAML/TS <c>llm_override:</c>
/// (incl. <c>response_format: json_object</c>) silently drops and the
/// provider never receives the field.
/// </summary>
public class CrewFactoryLlmOverrideTests
{
    private static (CrewFactory factory, InMemoryTaskRepository taskRepo) BuildFactory()
    {
        var loaderMock = new MockCrewDefinitionLoader();
        loaderMock.SetValidateResult(new CrewDefinitionValidationResult(true, Array.Empty<string>(), Array.Empty<string>()));
        var unitOfWork = new NullUnitOfWork();
        var taskRepo = new InMemoryTaskRepository(unitOfWork);
        var factory = new CrewFactory(
            loaderMock,
            new MockToolRegistry(),
            NullLogger<CrewFactory>.Instance,
            new InMemoryCrewRepository(unitOfWork),
            new InMemoryAgentRepository(unitOfWork),
            taskRepo);
        return (factory, taskRepo);
    }

    private static async System.Threading.Tasks.Task<CrewTask> FirstTaskAsync(Orkeon.Domain.Crew.Crew crew, InMemoryTaskRepository taskRepo)
    {
        var taskId = Assert.Single(crew.Tasks);
        var task = await taskRepo.GetByIdAsync(taskId);
        Assert.NotNull(task);
        return task!;
    }

    private static CrewConfiguration BuildConfigWithTaskOverride(LlmConfigOverride? llmOverride)
    {
        var agentId = AgentId.Create();
        return new CrewConfiguration
        {
            Name = "test-crew",
            Goal = "test",
            Process = ProcessType.Sequential,
            Agents =
            [
                new AgentConfiguration
                {
                    Id = agentId,
                    Role = "worker",
                    Goal = "do work",
                    Backstory = "b"
                }
            ],
            Tasks =
            [
                new TaskConfiguration
                {
                    Id = TaskId.Create(),
                    Description = "emit json",
                    ExpectedOutput = "json",
                    AssignedAgentId = agentId,
                    LlmOverride = llmOverride
                }
            ]
        };
    }

    [Fact]
    public async Task TaskLlmOverride_ResponseFormat_PropagatesToCrewTask()
    {
        var (factory, taskRepo) = BuildFactory();
        var config = BuildConfigWithTaskOverride(
            LlmConfigOverride.ForResponseFormat(LlmResponseFormat.JsonObject()));

        var crew = await factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken);

        var task = await FirstTaskAsync(crew, taskRepo);
        Assert.NotNull(task.LlmOverride);
        Assert.NotNull(task.LlmOverride!.ResponseFormat);
        Assert.Equal("json_object", task.LlmOverride.ResponseFormat!.Type);
    }

    [Fact]
    public async Task TaskLlmOverride_FullBlock_PropagatesAllFieldsToCrewTask()
    {
        var (factory, taskRepo) = BuildFactory();
        var fullOverride = new LlmConfigOverride
        {
            ResponseFormat = LlmResponseFormat.JsonObject(),
            Temperature = 0.0,
            MaxTokens = 256,
            TopP = 0.5
        };
        var config = BuildConfigWithTaskOverride(fullOverride);

        var crew = await factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken);

        var task = await FirstTaskAsync(crew, taskRepo);
        Assert.NotNull(task.LlmOverride);
        Assert.Equal("json_object", task.LlmOverride!.ResponseFormat!.Type);
        Assert.Equal(0.0, task.LlmOverride.Temperature);
        Assert.Equal(256, task.LlmOverride.MaxTokens);
        Assert.Equal(0.5, task.LlmOverride.TopP);
    }

    [Fact]
    public async Task TaskWithoutLlmOverride_StaysNullOnCrewTask()
    {
        var (factory, taskRepo) = BuildFactory();
        var config = BuildConfigWithTaskOverride(llmOverride: null);

        var crew = await factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken);

        var task = await FirstTaskAsync(crew, taskRepo);
        Assert.Null(task.LlmOverride);
    }
}
