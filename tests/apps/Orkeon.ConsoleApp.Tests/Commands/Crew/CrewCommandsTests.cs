using Orkeon.ConsoleApp.Commands.Crew;

namespace Orkeon.ConsoleApp.Tests.Commands.Crew;

public sealed class CrewCommandsTests
{
    [Fact]
    public void ListCrewsCommand_has_correct_name()
        => Assert.Equal("crew list", new ListCrewsCommand().Name);

    [Fact]
    public void CreateCrewCommand_has_correct_name()
        => Assert.Equal("crew create", new CreateCrewCommand().Name);

    [Fact]
    public void AddAgentToCrewCommand_has_correct_name()
        => Assert.Equal("crew add-agent", new AddAgentToCrewCommand().Name);

    [Fact]
    public void DeleteCrewCommand_has_correct_name()
        => Assert.Equal("crew delete", new DeleteCrewCommand().Name);

    [Fact]
    public void RunCrewCommand_has_correct_name()
        => Assert.Equal("crew run", new RunCrewCommand().Name);

    [Fact]
    public void All_crew_commands_implement_IMainMenuCommand()
    {
        Assert.True(typeof(Orkeon.ConsoleApp.Commands.IMainMenuCommand).IsAssignableFrom(typeof(ListCrewsCommand)));
        Assert.True(typeof(Orkeon.ConsoleApp.Commands.IMainMenuCommand).IsAssignableFrom(typeof(CreateCrewCommand)));
        Assert.True(typeof(Orkeon.ConsoleApp.Commands.IMainMenuCommand).IsAssignableFrom(typeof(AddAgentToCrewCommand)));
        Assert.True(typeof(Orkeon.ConsoleApp.Commands.IMainMenuCommand).IsAssignableFrom(typeof(DeleteCrewCommand)));
        Assert.True(typeof(Orkeon.ConsoleApp.Commands.IMainMenuCommand).IsAssignableFrom(typeof(RunCrewCommand)));
    }

    [Fact]
    public void All_crew_commands_have_descriptions()
    {
        Assert.NotEmpty(new ListCrewsCommand().Description);
        Assert.NotEmpty(new CreateCrewCommand().Description);
        Assert.NotEmpty(new AddAgentToCrewCommand().Description);
        Assert.NotEmpty(new DeleteCrewCommand().Description);
        Assert.NotEmpty(new RunCrewCommand().Description);
    }

    [Fact]
    public void All_crew_commands_have_empty_aliases()
    {
        Assert.Empty(new ListCrewsCommand().Aliases);
        Assert.Empty(new CreateCrewCommand().Aliases);
        Assert.Empty(new AddAgentToCrewCommand().Aliases);
        Assert.Empty(new DeleteCrewCommand().Aliases);
        Assert.Empty(new RunCrewCommand().Aliases);
    }
}
