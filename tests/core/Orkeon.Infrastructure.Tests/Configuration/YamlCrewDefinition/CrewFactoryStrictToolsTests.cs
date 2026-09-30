using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces;
using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Persistence.Crew;
using Orkeon.Infrastructure.Persistence.Task;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tests.MCP;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// Covers <see cref="CrewFactory"/> tool resolution in both strict and lenient modes
/// (P0-4). A crew referencing a tool absent from the registry must fail loudly under
/// <see cref="CrewFactoryOptions.StrictTools"/> and degrade silently otherwise — never
/// returning <c>null</c> tools.
/// </summary>
public class CrewFactoryStrictToolsTests
{
    private static (CrewFactory factory, InMemoryAgentRepository agentRepo, MockToolRegistry registry) BuildFactory(
        bool strictTools)
    {
        var loaderMock = new MockCrewDefinitionLoader();
        loaderMock.SetValidateResult(new CrewDefinitionValidationResult(true, Array.Empty<string>(), Array.Empty<string>()));
        var unitOfWork = new NullUnitOfWork();
        var agentRepo = new InMemoryAgentRepository(unitOfWork);
        var registry = new MockToolRegistry();
        var factory = new CrewFactory(
            loaderMock,
            registry,
            NullLogger<CrewFactory>.Instance,
            new InMemoryCrewRepository(unitOfWork),
            agentRepo,
            new InMemoryTaskRepository(unitOfWork),
            Options.Create(new CrewFactoryOptions { StrictTools = strictTools }));
        return (factory, agentRepo, registry);
    }

    private static CrewConfiguration BuildConfigWithAgentTools(params string[] toolNames)
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
                    Backstory = "b",
                    Tools = toolNames
                }
            ],
            Tasks =
            [
                new TaskConfiguration
                {
                    Id = TaskId.Create(),
                    Description = "do something",
                    ExpectedOutput = "output",
                    AssignedAgentId = agentId
                }
            ]
        };
    }

    [Fact]
    public async Task StrictMode_MissingTool_ThrowsWithMissingAndAvailableNames()
    {
        var (factory, _, registry) = BuildFactory(strictTools: true);
        registry.AddTool("known_tool", new MockTool("known_tool"));
        var config = BuildConfigWithAgentTools("ghost_tool");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken));

        Assert.Contains("ghost_tool", ex.Message, StringComparison.Ordinal);
        Assert.Contains("known_tool", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StrictMode_AllToolsPresent_CreatesCrewWithResolvedTool()
    {
        var (factory, agentRepo, registry) = BuildFactory(strictTools: true);
        registry.AddTool("known_tool", new MockTool("known_tool"));
        var config = BuildConfigWithAgentTools("known_tool");

        var crew = await factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken);

        var agent = await agentRepo.GetByIdAsync(Assert.Single(crew.Agents), TestContext.Current.CancellationToken);
        Assert.NotNull(agent);
        Assert.Contains(agent!.Tools, t => t.Name == "known_tool");
    }

    // GAP-01: a tool that implements IBaseTool alone (the rag_* tools, McpToolAdapter, any
    // plugin tool) is a tool like any other. It used to be found in the registry, dropped by
    // a filter on an empty marker interface (`ITool`, since deleted), and then reported as
    // unknown AND available in the same message.

    [Fact]
    public async Task StrictMode_RegisteredBaseOnlyTool_IsAttached()
    {
        var (factory, agentRepo, registry) = BuildFactory(strictTools: true);
        registry.AddTool("base_only", new StubBaseTool("base_only"));
        var config = BuildConfigWithAgentTools("base_only");

        var crew = await factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken);

        var agent = await agentRepo.GetByIdAsync(Assert.Single(crew.Agents), TestContext.Current.CancellationToken);
        Assert.NotNull(agent);
        Assert.Contains(agent!.Tools, t => t.Name == "base_only");
    }

    [Fact]
    public async Task LenientMode_RegisteredBaseOnlyTool_IsAttached()
    {
        var (factory, agentRepo, registry) = BuildFactory(strictTools: false);
        registry.AddTool("base_only", new StubBaseTool("base_only"));
        var config = BuildConfigWithAgentTools("base_only");

        var crew = await factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken);

        var agent = await agentRepo.GetByIdAsync(Assert.Single(crew.Agents), TestContext.Current.CancellationToken);
        Assert.NotNull(agent);
        Assert.Contains(agent!.Tools, t => t.Name == "base_only");
    }

    [Fact]
    public async Task StrictMode_ConnectedMcpTool_IsAttached()
    {
        var (factory, agentRepo, registry) = BuildFactory(strictTools: true);
        await using var transport = McpDiscoveryTransport.Exposing("search_issues");
        await using var provider = new McpToolProvider(registry);
        await provider.ConnectServerAsync("tracker", transport, TestContext.Current.CancellationToken);
        var config = BuildConfigWithAgentTools("search_issues");

        var crew = await factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken);

        var agent = await agentRepo.GetByIdAsync(Assert.Single(crew.Agents), TestContext.Current.CancellationToken);
        Assert.NotNull(agent);
        Assert.IsType<McpToolAdapter>(Assert.Single(agent!.Tools, t => t.Name == "search_issues"));
    }

    [Fact]
    public async Task LenientMode_MissingTool_SkipsToolAndCreatesCrew()
    {
        var (factory, agentRepo, registry) = BuildFactory(strictTools: false);
        registry.AddTool("known_tool", new MockTool("known_tool"));
        var config = BuildConfigWithAgentTools("known_tool", "ghost_tool");

        var crew = await factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken);

        var agent = await agentRepo.GetByIdAsync(Assert.Single(crew.Agents), TestContext.Current.CancellationToken);
        Assert.NotNull(agent);
        Assert.Contains(agent!.Tools, t => t.Name == "known_tool");
        Assert.DoesNotContain(agent.Tools, t => t.Name == "ghost_tool");
    }

    [Fact]
    public async Task LenientMode_AllToolsMissing_CreatesCrewWithNoTools()
    {
        var (factory, agentRepo, _) = BuildFactory(strictTools: false);
        var config = BuildConfigWithAgentTools("ghost_tool");

        var crew = await factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken);

        var agent = await agentRepo.GetByIdAsync(Assert.Single(crew.Agents), TestContext.Current.CancellationToken);
        Assert.NotNull(agent);
        Assert.Empty(agent!.Tools);
    }

    [Fact]
    public async Task DefaultOptions_MissingTool_IsLenient()
    {
        // No options passed → library default must be lenient (never throws, never null tools).
        var loaderMock = new MockCrewDefinitionLoader();
        loaderMock.SetValidateResult(new CrewDefinitionValidationResult(true, Array.Empty<string>(), Array.Empty<string>()));
        var unitOfWork = new NullUnitOfWork();
        var agentRepo = new InMemoryAgentRepository(unitOfWork);
        var factory = new CrewFactory(
            loaderMock,
            new MockToolRegistry(),
            NullLogger<CrewFactory>.Instance,
            new InMemoryCrewRepository(unitOfWork),
            agentRepo,
            new InMemoryTaskRepository(unitOfWork));
        var config = BuildConfigWithAgentTools("ghost_tool");

        var crew = await factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken);

        var agent = await agentRepo.GetByIdAsync(Assert.Single(crew.Agents), TestContext.Current.CancellationToken);
        Assert.NotNull(agent);
        Assert.Empty(agent!.Tools);
    }
}
