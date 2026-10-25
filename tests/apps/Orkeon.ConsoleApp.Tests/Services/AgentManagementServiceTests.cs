namespace Orkeon.ConsoleApp.Tests.Services;

public sealed class AgentManagementServiceTests
{
    [Fact]
    public async Task ShouldCreateAgentWithSelectedTools_WhenPromptsAreAnswered()
    {
        using var h = new ManagementServicesHarness();
        h.Lines("", "Ada", "Researcher", "Find facts", "Grew up in a library", "1, 3, x, 9");
        h.Keys(ConsoleKey.Enter);

        await h.Agents.CreateAgentAsync();

        var command = Assert.Single(h.AgentHandler.Received);
        Assert.Equal("Researcher", command.Role);
        Assert.Equal("Find facts", command.Goal);
        Assert.Equal("Grew up in a library", command.Backstory);
        Assert.Equal(["WebSearch", "FileWrite"], command.Tools);
        Assert.Single(h.Agents.Agents);
        Assert.True(h.Console.Contains("This field is required"));
        Assert.True(h.Console.Contains("Agent 'Ada' created successfully!"));
        Assert.True(h.Logged("Created agent: Ada with role: Researcher"));
    }

    [Fact]
    public async Task ShouldUseDefaultBackstory_WhenBackstoryIsSkipped()
    {
        using var h = new ManagementServicesHarness();
        h.Lines("Ada", "Researcher", "Find facts", "", "");
        h.Keys(ConsoleKey.Enter);

        await h.Agents.CreateAgentAsync();

        var command = Assert.Single(h.AgentHandler.Received);
        Assert.Equal("No backstory provided", command.Backstory);
        Assert.Empty(command.Tools!);
    }

    [Fact]
    public async Task ShouldCreateAgentWithoutPrompting_WhenValuesAreGiven()
    {
        using var h = new ManagementServicesHarness();

        await h.Agents.CreateAgentAsync("Bob", "Writer", "Write", "None");

        var command = Assert.Single(h.AgentHandler.Received);
        Assert.Null(command.Tools);
        Assert.Equal("None", command.Backstory);
        Assert.Single(h.Agents.Agents);
        Assert.True(h.Logged("Created agent: Bob with role: Writer"));
    }

    [Fact]
    public async Task ShouldSayNoAgents_WhenListingAnEmptyRoster()
    {
        using var h = new ManagementServicesHarness();
        h.Keys(ConsoleKey.Enter);

        await h.Agents.ListAgentsAsync();

        Assert.True(h.Console.Contains("No agents created yet."));
    }

    [Fact]
    public async Task ShouldListAgentsTruncatingLongValues_WhenAgentsExist()
    {
        using var h = new ManagementServicesHarness();
        await h.Agents.CreateAgentAsync("Bob", "Writer", new string('g', 60), "None");
        h.Keys(ConsoleKey.Enter);

        await h.Agents.ListAgentsAsync();

        Assert.True(h.Console.Contains("Total agents: 1"));
        // Ids longer than 20 characters and goals longer than 40 are cut with an ellipsis.
        Assert.True(h.Console.Contains(new string('g', 37) + "..."));
        Assert.False(h.Console.Contains(new string('g', 38)));
    }

    [Fact]
    public async Task ShouldSayNothingToDelete_WhenRosterIsEmpty()
    {
        using var h = new ManagementServicesHarness();
        h.Keys(ConsoleKey.Enter);

        await h.Agents.DeleteAgentAsync();

        Assert.True(h.Console.Contains("No agents to delete."));
    }

    [Fact]
    public async Task ShouldDeleteAgent_WhenDeletionIsConfirmed()
    {
        using var h = new ManagementServicesHarness();
        await h.Agents.CreateAgentAsync("Bob", "Writer", "Write", "None");
        await h.Agents.CreateAgentAsync("Eve", "Editor", "Edit", "None");
        var second = h.Agents.Agents[1].Id;
        h.Lines("0", "2");
        h.Keys(ConsoleKey.A, ConsoleKey.Y, ConsoleKey.Enter);

        await h.Agents.DeleteAgentAsync();

        var remaining = Assert.Single(h.Agents.Agents);
        Assert.NotEqual(second, remaining.Id);
        Assert.True(h.Console.Contains("Invalid choice"));
        Assert.True(h.Console.Contains($"Agent '{second}' deleted successfully!"));
        Assert.True(h.Logged($"Deleted agent: {second}"));
    }

    [Fact]
    public async Task ShouldKeepAgent_WhenDeletionIsDeclined()
    {
        using var h = new ManagementServicesHarness();
        await h.Agents.CreateAgentAsync("Bob", "Writer", "Write", "None");
        h.Lines("1");
        h.Keys(ConsoleKey.N, ConsoleKey.Enter);

        await h.Agents.DeleteAgentAsync();

        Assert.Single(h.Agents.Agents);
        Assert.True(h.Console.Contains("Deletion cancelled."));
    }
}
