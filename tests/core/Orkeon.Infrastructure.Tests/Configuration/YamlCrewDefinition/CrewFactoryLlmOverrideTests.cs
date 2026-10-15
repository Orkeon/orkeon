using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.DependencyInjection;
using Orkeon.Infrastructure.DependencyInjection;
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
    [Trait("BR", "CH-08")]
    public async Task TaskLlmOverride_ResponseFormat_PropagatesToCrewTask()
    {
        var (factory, taskRepo) = BuildFactory();
        var config = BuildConfigWithTaskOverride(
            new LlmConfigOverride { ResponseFormat = LlmResponseFormat.JsonObject() });

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

    private static async Task<LlmConfigOverride?> LoadTaskOverrideAsync(string overrideYaml)
    {
        var yaml = $"""
            name: cache
            goal: Answer
            process: sequential
            agents:
              writer:
                role: Writer
                goal: Write the answer
            tasks:
              write:
                description: Write the answer
                expected_output: An answer
                agent: writer
                llm_override:
            {overrideYaml}
            """;
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton<Orkeon.Domain.FileSystem.IFileSystemService>(
            new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService());
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        await using var container = services.BuildServiceProvider();
        var config = await container.GetRequiredService<ICrewDefinitionLoader>()
            .LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);
        return Assert.Single(config.Tasks).LlmOverride;
    }

    [Fact]
    [Trait("BR", "CH-04")]
    public async Task ShouldMapTheTaskCache_WhenLlmOverrideWritesACacheBlock()
    {
        var mapped = await LoadTaskOverrideAsync("""
                    cache:
                      system: true
                      ttl: 1h
            """);

        Assert.NotNull(mapped);
        Assert.NotNull(mapped!.Cache);
        Assert.True(mapped.Cache!.CacheSystemPrompt);
        Assert.Equal("1h", mapped.Cache.Ttl);
    }

    [Fact]
    [Trait("BR", "CH-04")]
    public async Task ShouldReadAnOverrideHoldingOnlyACache_WhenNoOtherKeyIsWritten()
    {
        var mapped = await LoadTaskOverrideAsync("""
                    cache:
                      system: true
            """);

        Assert.NotNull(mapped);
        Assert.NotNull(mapped!.Cache);
    }

    [Fact]
    [Trait("BR", "CH-07")]
    public async Task ShouldRefuseTheLoadNamingTheTaskAndKey_WhenLlmOverrideMaxTokensIsZero()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateCrewFromYamlAsync(
            agentLlm: "",
            taskOverride: "max_tokens: 0"));

        Assert.Contains("Task 'write'", ex.Message);
        Assert.Contains("max_tokens", ex.Message);
        Assert.Contains("omit the key to keep the inherited cap", ex.Message);
    }

    [Fact]
    [Trait("BR", "CH-07")]
    public async Task ShouldRefuseTheLoadNamingTheAgentAndKey_WhenAgentMaxTokensIsNegative()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateCrewFromYamlAsync(
            agentLlm: "llm:\n      max_tokens: -1",
            taskOverride: null));

        Assert.Contains("writer", ex.Message);
        Assert.Contains("max_tokens", ex.Message);
        Assert.Contains("omit the key to keep the inherited cap", ex.Message);
    }

    private static async System.Threading.Tasks.Task CreateCrewFromYamlAsync(string agentLlm, string? taskOverride)
    {
        var yaml = "name: mt\ngoal: Answer\nprocess: sequential\nagents:\n  writer:\n    role: Writer\n    goal: Write\n"
            + (agentLlm.Length > 0 ? "    " + agentLlm + "\n" : "")
            + "tasks:\n  write:\n    description: Write\n    expected_output: An answer\n    agent: writer\n"
            + (taskOverride is null ? "" : "    llm_override:\n      " + taskOverride + "\n");
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton<Orkeon.Domain.FileSystem.IFileSystemService>(
            new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService());
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        await using var container = services.BuildServiceProvider();
        var loader = container.GetRequiredService<ICrewDefinitionLoader>();
        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);
        var unitOfWork = new NullUnitOfWork();
        var factory = new CrewFactory(
            loader,
            new MockToolRegistry(),
            NullLogger<CrewFactory>.Instance,
            new InMemoryCrewRepository(unitOfWork),
            new InMemoryAgentRepository(unitOfWork),
            new InMemoryTaskRepository(unitOfWork));
        await factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken);
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
