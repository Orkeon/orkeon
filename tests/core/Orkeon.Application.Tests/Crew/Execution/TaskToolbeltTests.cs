using Orkeon.Application.Crew.Execution;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Task;
using Orkeon.Domain.Tools;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>
/// #448: a host's tool collection holds a null for a tool its container withholds (<c>system_one_decide</c> with the
/// decision circuit off, a dropped plugin tool). The composition of a task's belt skips it rather than reading its name.
/// </summary>
public sealed class TaskToolbeltTests
{
    [Fact]
    [Trait("BR", "CH-448")]
    public void ShouldSkipAWithheldTool_WhenTheRegisteredToolsHoldANull()
    {
        // Arrange
        var agent = new AgentBuilder().Role("Writer").Goal("Write").Build();
        var task = new CrewTaskBuilder().Description("Ask").ExpectedOutput("An answer").HumanInput().Build();
        var humanInput = new StubBaseTool(TaskToolbelt.HumanInputToolName);
        IBaseTool[] registered = [null!, humanInput];

        // Act
        var belt = TaskToolbelt.Compose(agent, task, registered);

        // Assert
        Assert.Same(humanInput, Assert.Single(belt));
    }
}
