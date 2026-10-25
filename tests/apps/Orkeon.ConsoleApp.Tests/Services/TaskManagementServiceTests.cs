namespace Orkeon.ConsoleApp.Tests.Services;

public sealed class TaskManagementServiceTests
{
    [Fact]
    public async Task ShouldCreateTask_WhenPromptsAreAnswered()
    {
        using var h = new ManagementServicesHarness();
        h.Lines("Summarise the report", "  ", "A summary");
        h.Keys(ConsoleKey.Enter);

        await h.Tasks.CreateTaskAsync();

        var command = Assert.Single(h.TaskHandler.Received);
        Assert.Equal("Summarise the report", command.Description);
        Assert.Equal("A summary", command.ExpectedOutput);
        Assert.Null(command.AgentId);
        Assert.Single(h.Tasks.Tasks);
        Assert.True(h.Console.Contains("Task created successfully!"));
        Assert.True(h.Console.Contains("Expected Output: A summary"));
        Assert.True(h.Logged("Created task: Summarise the report"));
    }

    [Fact]
    public async Task ShouldCreateTaskWithoutPrompting_WhenValuesAreGiven()
    {
        using var h = new ManagementServicesHarness();

        await h.Tasks.CreateTaskAsync("Translate", "French text");

        Assert.Equal("Translate", Assert.Single(h.Tasks.Tasks).Description);
        Assert.True(h.Logged("Created task: Translate"));
    }

    [Fact]
    public async Task ShouldSayNoTasks_WhenListingAnEmptyBoard()
    {
        using var h = new ManagementServicesHarness();
        h.Keys(ConsoleKey.Enter);

        await h.Tasks.ListTasksAsync();

        Assert.True(h.Console.Contains("No tasks created yet."));
    }

    [Fact]
    public async Task ShouldListAssignedAndUnassignedTasks_WhenTasksExist()
    {
        using var h = new ManagementServicesHarness();
        await h.Tasks.CreateTaskAsync("First", "One");
        h.TaskHandler.AssignedAgent = "agent-7";
        await h.Tasks.CreateTaskAsync("Second", "Two");
        h.Keys(ConsoleKey.Enter);

        await h.Tasks.ListTasksAsync();

        Assert.True(h.Console.Contains("Total tasks: 2"));
        Assert.True(h.Console.Contains("Assigned to: [Not assigned]"));
        Assert.True(h.Console.Contains("Assigned to: agent-7"));
        Assert.True(h.Console.Contains("Expected Output: Two"));
    }

    [Fact]
    public async Task ShouldSayNoTasksToAssign_WhenBoardIsEmpty()
    {
        using var h = new ManagementServicesHarness();
        h.Keys(ConsoleKey.Enter);

        await h.Tasks.AssignTaskAsync();

        Assert.True(h.Console.Contains("No tasks available. Please create tasks first."));
    }

    [Fact]
    public async Task ShouldSelectTaskForAssignment_WhenTasksExist()
    {
        using var h = new ManagementServicesHarness();
        await h.Tasks.CreateTaskAsync(new string('d', 70), "One");
        h.TaskHandler.AssignedAgent = "agent-7";
        await h.Tasks.CreateTaskAsync("Second", "Two");
        h.Lines("2");
        h.Keys(ConsoleKey.Enter);

        await h.Tasks.AssignTaskAsync();

        Assert.True(h.Console.Contains(new string('d', 47) + "... [Unassigned]"));
        Assert.True(h.Console.Contains("Second [Assigned to agent-7]"));
        Assert.True(h.Console.Contains("Task 'Second' selected for assignment."));
        Assert.True(h.Logged("Task assignment initiated for: Second"));
    }

    [Fact]
    public async Task ShouldSayNothingToDelete_WhenBoardIsEmpty()
    {
        using var h = new ManagementServicesHarness();
        h.Keys(ConsoleKey.Enter);

        await h.Tasks.DeleteTaskAsync();

        Assert.True(h.Console.Contains("No tasks to delete."));
    }

    [Fact]
    public async Task ShouldDeleteTask_WhenDeletionIsConfirmed()
    {
        using var h = new ManagementServicesHarness();
        await h.Tasks.CreateTaskAsync(new string('d', 70), "One");
        await h.Tasks.CreateTaskAsync("Second", "Two");
        h.Lines("1");
        h.Keys(ConsoleKey.Y, ConsoleKey.Enter);

        await h.Tasks.DeleteTaskAsync();

        Assert.Equal("Second", Assert.Single(h.Tasks.Tasks).Description);
        Assert.True(h.Console.Contains(new string('d', 57) + "..."));
        Assert.True(h.Console.Contains("Task deleted successfully!"));
        Assert.True(h.Logged("Deleted task: " + new string('d', 70)));
    }

    [Fact]
    public async Task ShouldKeepTask_WhenDeletionIsDeclined()
    {
        using var h = new ManagementServicesHarness();
        await h.Tasks.CreateTaskAsync("Only", "One");
        h.Lines("1");
        h.Keys(ConsoleKey.N, ConsoleKey.Enter);

        await h.Tasks.DeleteTaskAsync();

        Assert.Single(h.Tasks.Tasks);
        Assert.True(h.Console.Contains("Deletion cancelled."));
    }
}
