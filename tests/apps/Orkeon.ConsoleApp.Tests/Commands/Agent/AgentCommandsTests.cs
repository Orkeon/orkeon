using Orkeon.ConsoleApp.Commands.Agent;

namespace Orkeon.ConsoleApp.Tests.Commands.Agent;

public sealed class AgentCommandsTests
{
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
