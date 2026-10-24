using Orkeon.ConsoleApp.Commands.Task;
using Orkeon.ConsoleApp.Tests.Services;

namespace Orkeon.ConsoleApp.Tests.Commands.TaskCmds;

[Trait("BR", "CH-08")]
public sealed class TaskCommandsTests
{
    [Fact]
    public async Task CreateTaskCommand_creates_a_task_through_the_service()
    {
        using var h = new ManagementServicesHarness();
        h.Lines("Write", "Text");
        h.Keys(ConsoleKey.Enter);

        var result = await new CreateTaskCommand().ExecuteAsync(h.Context("task create"), CancellationToken.None);

        Assert.False(result.ShouldExit);
        Assert.Single(h.Tasks.Tasks);
    }

    [Fact]
    public async Task ListTasksCommand_reports_an_empty_board()
    {
        using var h = new ManagementServicesHarness();
        h.Keys(ConsoleKey.Enter);

        var result = await new ListTasksCommand().ExecuteAsync(h.Context("task list"), CancellationToken.None);

        Assert.False(result.ShouldExit);
        Assert.True(h.Console.Contains("No tasks created yet."));
    }

    [Fact]
    public async Task AssignTaskCommand_reports_an_empty_board()
    {
        using var h = new ManagementServicesHarness();
        h.Keys(ConsoleKey.Enter);

        var result = await new AssignTaskCommand().ExecuteAsync(h.Context("task assign"), CancellationToken.None);

        Assert.False(result.ShouldExit);
        Assert.True(h.Console.Contains("No tasks available. Please create tasks first."));
    }

    [Fact]
    public async Task DeleteTaskCommand_reports_an_empty_board()
    {
        using var h = new ManagementServicesHarness();
        h.Keys(ConsoleKey.Enter);

        var result = await new DeleteTaskCommand().ExecuteAsync(h.Context("task delete"), CancellationToken.None);

        Assert.False(result.ShouldExit);
        Assert.True(h.Console.Contains("No tasks to delete."));
    }

    [Fact]
    public async Task Task_commands_reject_a_null_context()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => new CreateTaskCommand().ExecuteAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new ListTasksCommand().ExecuteAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new AssignTaskCommand().ExecuteAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new DeleteTaskCommand().ExecuteAsync(null!, CancellationToken.None));
    }

    [Fact]
    public void ListTasksCommand_has_correct_name()
        => Assert.Equal("task list", new ListTasksCommand().Name);

    [Fact]
    public void CreateTaskCommand_has_correct_name()
        => Assert.Equal("task create", new CreateTaskCommand().Name);

    [Fact]
    public void AssignTaskCommand_has_correct_name()
        => Assert.Equal("task assign", new AssignTaskCommand().Name);

    [Fact]
    public void DeleteTaskCommand_has_correct_name()
        => Assert.Equal("task delete", new DeleteTaskCommand().Name);

    [Fact]
    public void All_task_commands_implement_IMainMenuCommand()
    {
        Assert.True(typeof(Orkeon.ConsoleApp.Commands.IMainMenuCommand).IsAssignableFrom(typeof(ListTasksCommand)));
        Assert.True(typeof(Orkeon.ConsoleApp.Commands.IMainMenuCommand).IsAssignableFrom(typeof(CreateTaskCommand)));
        Assert.True(typeof(Orkeon.ConsoleApp.Commands.IMainMenuCommand).IsAssignableFrom(typeof(AssignTaskCommand)));
        Assert.True(typeof(Orkeon.ConsoleApp.Commands.IMainMenuCommand).IsAssignableFrom(typeof(DeleteTaskCommand)));
    }

    [Fact]
    public void All_task_commands_have_descriptions()
    {
        Assert.NotEmpty(new ListTasksCommand().Description);
        Assert.NotEmpty(new CreateTaskCommand().Description);
        Assert.NotEmpty(new AssignTaskCommand().Description);
        Assert.NotEmpty(new DeleteTaskCommand().Description);
    }

    [Fact]
    public void All_task_commands_have_empty_aliases()
    {
        Assert.Empty(new ListTasksCommand().Aliases);
        Assert.Empty(new CreateTaskCommand().Aliases);
        Assert.Empty(new AssignTaskCommand().Aliases);
        Assert.Empty(new DeleteTaskCommand().Aliases);
    }
}
