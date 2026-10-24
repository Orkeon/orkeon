namespace Orkeon.ConsoleApp.Tests.Services;

public sealed class CrewManagementServiceTests
{
    [Fact]
    public async Task ShouldCreateHierarchicalCrewAndReportNoAgents_WhenAgentsAreWantedButNoneExist()
    {
        using var h = new ManagementServicesHarness();
        h.Lines("Alpha", "", "7", "2");
        h.Keys(ConsoleKey.Y, ConsoleKey.Enter);

        await h.Crews.CreateCrewAsync();

        var command = Assert.Single(h.CrewHandler.Received);
        Assert.Equal("Alpha", command.Name);
        Assert.Equal("No description provided", command.Goal);
        Assert.Equal("Hierarchical", command.ProcessType.Value);
        Assert.Null(command.AgentIds);
        Assert.True(h.Console.Contains("Crew 'Alpha' created successfully with Hierarchical process!"));
        Assert.True(h.Console.Contains("No agents available. Please create agents first."));
        Assert.True(h.Logged("Created crew: Alpha"));
    }

    [Fact]
    public async Task ShouldAddChosenAgentsOnce_WhenCreatingASequentialCrew()
    {
        using var h = new ManagementServicesHarness();
        await h.Agents.CreateAgentAsync("Bob", "Writer", "Write", "None");
        await h.Agents.CreateAgentAsync("Eve", "Editor", "Edit", "None");
        var bob = h.Agents.Agents[0].Id;
        h.Lines("Beta", "Ship the docs", "1", " 1, 1, 9, z ");
        h.Keys(ConsoleKey.Y, ConsoleKey.Enter);

        await h.Crews.CreateCrewAsync();

        Assert.Equal("Sequential", Assert.Single(h.CrewHandler.Received).ProcessType.Value);
        Assert.Equal("Ship the docs", h.CrewHandler.Received[0].Goal);
        Assert.True(h.Console.Contains($"Added agent '{bob}' to crew 'Beta'"));
        Assert.True(h.Console.Contains($"Agent '{bob}' is already in the crew"));
        Assert.True(h.Logged($"Added agent {bob} to crew Beta"));
    }

    [Fact]
    public async Task ShouldNotPromptForAgents_WhenTheOfferIsDeclined()
    {
        using var h = new ManagementServicesHarness();
        h.Lines("Gamma", "desc", "1");
        h.Keys(ConsoleKey.N, ConsoleKey.Enter);

        await h.Crews.CreateCrewAsync();

        Assert.Single(h.Crews.Crews);
        Assert.False(h.Console.Contains("Available agents:"));
    }

    [Fact]
    public async Task ShouldCreateSequentialCrewWithoutPrompting_WhenValuesAreGiven()
    {
        using var h = new ManagementServicesHarness();

        await h.Crews.CreateCrewAsync("Delta", "Goal");

        var command = Assert.Single(h.CrewHandler.Received);
        Assert.Equal("Sequential", command.ProcessType.Value);
        Assert.Equal("Goal", command.Goal);
        Assert.True(h.Logged("Created crew: Delta"));
    }

    [Fact]
    public async Task ShouldSayNoCrews_WhenListingAnEmptyRoster()
    {
        using var h = new ManagementServicesHarness();
        h.Keys(ConsoleKey.Enter);

        await h.Crews.ListCrewsAsync();

        Assert.True(h.Console.Contains("No crews created yet."));
    }

    [Fact]
    public async Task ShouldListCrewsWithCounts_WhenCrewsExist()
    {
        using var h = new ManagementServicesHarness();
        await h.Crews.CreateCrewAsync(new string('n', 30), "Goal");
        h.Keys(ConsoleKey.Enter);

        await h.Crews.ListCrewsAsync();

        Assert.True(h.Console.Contains("Total crews: 1"));
        Assert.True(h.Console.Contains(new string('n', 22) + "..."));
        Assert.True(h.Console.Contains("Sequential"));
    }

    [Fact]
    public async Task ShouldRefuseToAddAgents_WhenNoCrewExists()
    {
        using var h = new ManagementServicesHarness();
        h.Keys(ConsoleKey.Enter);

        await h.Crews.AddAgentToCrewAsync();

        Assert.True(h.Console.Contains("No crews available. Please create a crew first."));
    }

    [Fact]
    public async Task ShouldMarkAgentsAlreadyInCrew_WhenAddingToAnExistingCrew()
    {
        using var h = new ManagementServicesHarness();
        await h.Agents.CreateAgentAsync("Bob", "Writer", "Write", "None");
        await h.Agents.CreateAgentAsync("Eve", "Editor", "Edit", "None");
        await h.Crews.CreateCrewAsync("Delta", "Goal");
        var eve = h.Agents.Agents[1].Id;
        h.Lines("1", "2");
        h.Keys(ConsoleKey.Enter);
        await h.Crews.AddAgentToCrewAsync();
        h.Console.Lines.Clear();
        h.Lines("1", "");
        h.Keys(ConsoleKey.Enter);

        await h.Crews.AddAgentToCrewAsync();

        Assert.True(h.Console.Contains($"2. {eve} - Editor [Already in crew]"));
        Assert.False(h.Console.Contains("Added agent"));
    }

    [Fact]
    public async Task ShouldSayNothingToDelete_WhenNoCrewExists()
    {
        using var h = new ManagementServicesHarness();
        h.Keys(ConsoleKey.Enter);

        await h.Crews.DeleteCrewAsync();

        Assert.True(h.Console.Contains("No crews to delete."));
    }

    [Fact]
    public async Task ShouldDeleteCrew_WhenDeletionIsConfirmed()
    {
        using var h = new ManagementServicesHarness();
        await h.Crews.CreateCrewAsync("Delta", "Goal");
        h.Lines("1");
        h.Keys(ConsoleKey.Y, ConsoleKey.Enter);

        await h.Crews.DeleteCrewAsync();

        Assert.Empty(h.Crews.Crews);
        Assert.True(h.Console.Contains("Crew 'Delta' deleted successfully!"));
        Assert.True(h.Logged("Deleted crew: Delta"));
    }

    [Fact]
    public async Task ShouldKeepCrew_WhenDeletionIsDeclined()
    {
        using var h = new ManagementServicesHarness();
        await h.Crews.CreateCrewAsync("Delta", "Goal");
        h.Lines("1");
        h.Keys(ConsoleKey.N, ConsoleKey.Enter);

        await h.Crews.DeleteCrewAsync();

        Assert.Single(h.Crews.Crews);
        Assert.True(h.Console.Contains("Deletion cancelled."));
    }

    [Fact]
    public async Task ShouldRefuseToExecute_WhenNoCrewExists()
    {
        using var h = new ManagementServicesHarness();
        h.Keys(ConsoleKey.Enter);

        await h.Crews.ExecuteCrewAsync();

        Assert.True(h.Console.Contains("No crews available. Please create a crew first."));
    }

    [Fact]
    public async Task ShouldRefuseToExecute_WhenCrewHasNoAgents()
    {
        using var h = new ManagementServicesHarness();
        await h.Crews.CreateCrewAsync("Delta", "Goal");
        h.Lines("1");
        h.Keys(ConsoleKey.Enter);

        await h.Crews.ExecuteCrewAsync();

        Assert.True(h.Console.Contains("1. Delta (0 agents, 0 tasks)"));
        Assert.True(h.Console.Contains("Crew 'Delta' has no agents. Please add agents first."));
    }

    [Fact]
    public async Task ShouldCancelExecution_WhenNoTaskExists()
    {
        using var h = await CrewWithOneAgentAsync();
        h.Lines("1");
        h.Keys(ConsoleKey.Enter);

        await h.Crews.ExecuteCrewAsync();

        Assert.True(h.Console.Contains("No tasks selected. Execution cancelled."));
    }

    [Fact]
    public async Task ShouldCancelExecution_WhenNoTaskIsSelected()
    {
        using var h = await CrewWithOneAgentAsync();
        await h.Tasks.CreateTaskAsync("Write", "Text");
        h.Lines("1", "  ");
        h.Keys(ConsoleKey.Enter);

        await h.Crews.ExecuteCrewAsync();

        Assert.True(h.Console.Contains("1. Write"));
        Assert.True(h.Console.Contains("No tasks selected. Execution cancelled."));
    }

    [Fact]
    public async Task ShouldSimulateSequentialRunAndKeepSelectedTasks_WhenTasksAreSelected()
    {
        using var h = await CrewWithOneAgentAsync();
        await h.Tasks.CreateTaskAsync("Write", "Text");
        var agent = h.Agents.Agents[0].Id;
        h.Lines("1", "1, 4, q");
        h.Keys(ConsoleKey.Enter);

        await h.Crews.ExecuteCrewAsync();

        Assert.True(h.Console.Contains("[SIMULATION] Executing crew 'Delta'..."));
        Assert.True(h.Console.Contains("Process type: Sequential"));
        Assert.True(h.Console.Contains("Tasks to execute: 1"));
        Assert.True(h.Console.Contains($"[SIMULATION] {agent} is working on the task..."));
        Assert.True(h.Console.Contains("[SIMULATION] Crew execution completed!"));
        Assert.True(h.Logged("Executed crew: Delta with 1 tasks"));

        // The selected tasks stay with the crew: the next run does not prompt for them again.
        h.Console.Lines.Clear();
        h.Lines("1");
        h.Keys(ConsoleKey.Enter);
        await h.Crews.ExecuteCrewAsync();
        Assert.True(h.Console.Contains("1. Delta (1 agents, 1 tasks)"));
        Assert.False(h.Console.Contains("Available tasks:"));
    }

    [Fact]
    public async Task ShouldDelegateFromTheFirstAgent_WhenCrewIsHierarchical()
    {
        using var h = new ManagementServicesHarness();
        await h.Agents.CreateAgentAsync("Bob", "Manager", "Lead", "None");
        await h.Agents.CreateAgentAsync("Eve", "Editor", "Edit", "None");
        var manager = h.Agents.Agents[0].Id;
        var worker = h.Agents.Agents[1].Id;
        await h.Tasks.CreateTaskAsync("Write", "Text");
        h.Lines("Omega", "", "2", "1,2");
        h.Keys(ConsoleKey.Y, ConsoleKey.Enter);
        await h.Crews.CreateCrewAsync();
        h.Lines("1", "1");
        h.Keys(ConsoleKey.Enter);

        await h.Crews.ExecuteCrewAsync();

        Assert.True(h.Console.Contains("Process type: Hierarchical"));
        Assert.True(h.Console.Contains($"[SIMULATION] Manager {manager} is delegating work..."));
        Assert.True(h.Console.Contains($"[SIMULATION] {worker} received delegation..."));
        Assert.True(h.Console.Contains("[SIMULATION] Task completed!"));
    }

    private static async Task<ManagementServicesHarness> CrewWithOneAgentAsync()
    {
        var h = new ManagementServicesHarness();
        await h.Agents.CreateAgentAsync("Bob", "Writer", "Write", "None");
        await h.Crews.CreateCrewAsync("Delta", "Goal");
        h.Lines("1", "1");
        h.Keys(ConsoleKey.Enter);
        await h.Crews.AddAgentToCrewAsync();
        h.Console.Lines.Clear();
        return h;
    }
}
