using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces;
using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Persistence.Crew;
using Orkeon.Infrastructure.Persistence.Task;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// #392: the load warns, once per task, when a <c>tool_call</c> deliverable's agent has no
/// <c>file_write</c> — YAML and <c>.ork.ts</c> crews both reach <see cref="CrewFactory"/>.
/// </summary>
public sealed class CrewFactoryDeliverableToolTests
{
    private static (CrewFactory Factory, MockLogger<CrewFactory> Logger) BuildFactory()
    {
        var loader = new MockCrewDefinitionLoader();
        loader.SetValidateResult(new CrewDefinitionValidationResult(true, Array.Empty<string>(), Array.Empty<string>()));
        var unitOfWork = new NullUnitOfWork();
        var registry = new MockToolRegistry();
        registry.AddTool("file_read", new MockTool("file_read"));
        registry.AddTool("file_write", new MockTool("file_write"));
        var logger = new MockLogger<CrewFactory>();
        var factory = new CrewFactory(
            loader,
            registry,
            logger,
            new InMemoryCrewRepository(unitOfWork),
            new InMemoryAgentRepository(unitOfWork),
            new InMemoryTaskRepository(unitOfWork),
            Options.Create(new CrewFactoryOptions { StrictTools = true }));
        return (factory, logger);
    }

    private static CrewConfiguration Config(string[] agentTools, DeliverableSource source)
    {
        var agentId = AgentId.Create();
        return new CrewConfiguration
        {
            Name = "notes-crew",
            Goal = "Take notes",
            Process = ProcessType.Sequential,
            Agents =
            [
                new AgentConfiguration
                {
                    Id = agentId,
                    Role = "Reader",
                    Goal = "Read the sources",
                    Backstory = "b",
                    Tools = agentTools,
                },
            ],
            Tasks =
            [
                new TaskConfiguration
                {
                    Id = TaskId.Create(),
                    Key = "take_notes",
                    Description = "Read the sources and take notes.",
                    ExpectedOutput = "The notes.",
                    AssignedAgentId = agentId,
                    Deliverable = new TaskDeliverable { Path = "/output/notes.md", Source = source },
                },
            ],
        };
    }

    private static List<string> DeliverableWarnings(MockLogger<CrewFactory> logger) =>
        logger.LogEntries
            .Where(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("no file_write tool", StringComparison.Ordinal))
            .Select(entry => entry.Message)
            .ToList();

    [Fact]
    public async Task ShouldLogOneWarning_WhenAToolCallDeliverableAgentHasNoFileWrite()
    {
        var (factory, logger) = BuildFactory();

        await factory.CreateFromConfigAsync(Config(["file_read"], DeliverableSource.ToolCall), TestContext.Current.CancellationToken);

        var warning = Assert.Single(DeliverableWarnings(logger));
        Assert.Contains("Crew 'notes-crew': task 'take_notes' declares the deliverable '/output/notes.md'", warning, StringComparison.Ordinal);
        Assert.Contains("its agent 'Reader'", warning, StringComparison.Ordinal);
        Assert.Contains("set deliverable.source: final_message", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldNotWarn_WhenTheAgentHasFileWrite()
    {
        var (factory, logger) = BuildFactory();

        await factory.CreateFromConfigAsync(Config(["file_read", "file_write"], DeliverableSource.ToolCall), TestContext.Current.CancellationToken);

        Assert.Empty(DeliverableWarnings(logger));
    }

    [Fact]
    public async Task ShouldNotWarn_WhenTheDeliverableIsTheFinalMessage()
    {
        var (factory, logger) = BuildFactory();

        await factory.CreateFromConfigAsync(Config(["file_read"], DeliverableSource.FinalMessage), TestContext.Current.CancellationToken);

        Assert.Empty(DeliverableWarnings(logger));
    }
}
