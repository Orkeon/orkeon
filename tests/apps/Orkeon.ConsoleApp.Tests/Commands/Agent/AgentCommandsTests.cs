using Orkeon.ConsoleApp.Commands.Agent;
using Orkeon.ConsoleApp.Tests.Services;

namespace Orkeon.ConsoleApp.Tests.Commands.Agent;

public sealed class AgentCommandsTests
{
    [Fact]
    public async Task CreateAgentCommand_creates_an_agent_through_the_service()
    {
        using var h = new ManagementServicesHarness();
        h.Lines("Ada", "Researcher", "Find facts", "", "");
        h.Keys(ConsoleKey.Enter);

        var result = await new CreateAgentCommand().ExecuteAsync(h.Context("agent create"), CancellationToken.None);

        Assert.False(result.ShouldExit);
        Assert.Single(h.Agents.Agents);
    }

    [Fact]
    public async Task DeleteAgentCommand_reports_an_empty_roster()
    {
        using var h = new ManagementServicesHarness();
        h.Keys(ConsoleKey.Enter);

        var result = await new DeleteAgentCommand().ExecuteAsync(h.Context("agent delete"), CancellationToken.None);

        Assert.False(result.ShouldExit);
        Assert.True(h.Console.Contains("No agents to delete."));
    }

    [Fact]
    public async Task ListAgentsCommand_reports_an_empty_roster()
    {
        using var h = new ManagementServicesHarness();
        h.Keys(ConsoleKey.Enter);

        var result = await new ListAgentsCommand().ExecuteAsync(h.Context("agent list"), CancellationToken.None);

        Assert.False(result.ShouldExit);
        Assert.True(h.Console.Contains("No agents created yet."));
    }

    [Fact]
    public async Task Agent_commands_reject_a_null_context()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => new CreateAgentCommand().ExecuteAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new DeleteAgentCommand().ExecuteAsync(null!, CancellationToken.None));
    }

    [Fact]
    public void ListAgentsCommand_name_is_correct()
        => Assert.Equal("agent list", new ListAgentsCommand().Name);

    [Fact]
    public void CreateAgentCommand_name_is_correct()
        => Assert.Equal("agent create", new CreateAgentCommand().Name);

    [Fact]
    public void DeleteAgentCommand_name_is_correct()
        => Assert.Equal("agent delete", new DeleteAgentCommand().Name);

    [Fact]
    public void All_agent_commands_have_descriptions()
    {
        Assert.NotEmpty(new ListAgentsCommand().Description);
        Assert.NotEmpty(new CreateAgentCommand().Description);
        Assert.NotEmpty(new DeleteAgentCommand().Description);
    }

    [Fact]
    public void All_agent_commands_have_empty_aliases()
    {
        Assert.Empty(new ListAgentsCommand().Aliases);
        Assert.Empty(new CreateAgentCommand().Aliases);
        Assert.Empty(new DeleteAgentCommand().Aliases);
    }

    [Fact]
    public void All_agent_commands_implement_IMainMenuCommand()
    {
        Assert.True(typeof(Orkeon.ConsoleApp.Commands.IMainMenuCommand).IsAssignableFrom(typeof(ListAgentsCommand)));
        Assert.True(typeof(Orkeon.ConsoleApp.Commands.IMainMenuCommand).IsAssignableFrom(typeof(CreateAgentCommand)));
        Assert.True(typeof(Orkeon.ConsoleApp.Commands.IMainMenuCommand).IsAssignableFrom(typeof(DeleteAgentCommand)));
    }
}
