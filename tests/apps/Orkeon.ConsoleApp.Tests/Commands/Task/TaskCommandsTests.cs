using Orkeon.ConsoleApp.Commands.Task;

namespace Orkeon.ConsoleApp.Tests.Commands.TaskCmds;

public sealed class TaskCommandsTests
{
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
