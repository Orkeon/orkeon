using Orkeon.ConsoleApp.Commands.Crew;
using Orkeon.ConsoleApp.Tests.Services;

namespace Orkeon.ConsoleApp.Tests.Commands.Crew;

[Trait("BR", "CH-08")]
public sealed class CrewCommandsTests
{
    [Fact]
    public async Task CreateCrewCommand_creates_a_crew_through_the_service()
    {
        using var h = new ManagementServicesHarness();
        h.Lines("Alpha", "", "1");
        h.Keys(ConsoleKey.N, ConsoleKey.Enter);

        var result = await new CreateCrewCommand().ExecuteAsync(h.Context("crew create"), CancellationToken.None);

        Assert.False(result.ShouldExit);
        Assert.Single(h.Crews.Crews);
    }

    [Fact]
    public async Task ListCrewsCommand_reports_an_empty_roster()
    {
        using var h = new ManagementServicesHarness();
        h.Keys(ConsoleKey.Enter);

        var result = await new ListCrewsCommand().ExecuteAsync(h.Context("crew list"), CancellationToken.None);

        Assert.False(result.ShouldExit);
        Assert.True(h.Console.Contains("No crews created yet."));
    }

    [Fact]
    public async Task AddAgentToCrewCommand_reports_a_missing_crew()
    {
        using var h = new ManagementServicesHarness();
        h.Keys(ConsoleKey.Enter);

        var result = await new AddAgentToCrewCommand().ExecuteAsync(h.Context("crew add-agent"), CancellationToken.None);

        Assert.False(result.ShouldExit);
        Assert.True(h.Console.Contains("No crews available. Please create a crew first."));
    }

    [Fact]
    public async Task DeleteCrewCommand_reports_an_empty_roster()
    {
        using var h = new ManagementServicesHarness();
        h.Keys(ConsoleKey.Enter);

        var result = await new DeleteCrewCommand().ExecuteAsync(h.Context("crew delete"), CancellationToken.None);

        Assert.False(result.ShouldExit);
        Assert.True(h.Console.Contains("No crews to delete."));
    }

    [Fact]
    public async Task RunCrewCommand_reports_a_missing_crew()
    {
        using var h = new ManagementServicesHarness();
        h.Keys(ConsoleKey.Enter);

        var result = await new RunCrewCommand().ExecuteAsync(h.Context("crew run"), CancellationToken.None);

        Assert.False(result.ShouldExit);
        Assert.True(h.Console.Contains("No crews available. Please create a crew first."));
    }

    [Fact]
    public async Task Crew_commands_reject_a_null_context()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => new CreateCrewCommand().ExecuteAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new ListCrewsCommand().ExecuteAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new AddAgentToCrewCommand().ExecuteAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new DeleteCrewCommand().ExecuteAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new RunCrewCommand().ExecuteAsync(null!, CancellationToken.None));
    }

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
