using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;

namespace Orkeon.Application.Tests.Context;

public class SimpleExecutionContextTests
{
    private class TestMemoryScope : IMemoryScope
    {
        public string AgentId => "test-agent";
        public string ScopeId => "test-scope";
        public async System.Threading.Tasks.Task<T> ExecuteInScopeAsync<T>(Func<System.Threading.Tasks.Task<T>> op) => await op();
        public async System.Threading.Tasks.Task ExecuteInScopeAsync(Func<System.Threading.Tasks.Task> op) => await op();
        public void Dispose() { }
    }

    [Fact]
    public void Create_ShouldReturnContextWithCrewId()
    {
        var crewId = CrewId.Create();
        var input = new CrewInput("initial context", new Dictionary<string, object> { ["k"] = "v" });
        using var memory = new TestMemoryScope();

        var ctx = SimpleExecutionContext.Create(crewId, input, memory);

        Assert.Equal(crewId, ctx.CrewId);
    }

    [Fact]
    public void Create_ShouldMapStringVariablesFromInput()
    {
        var crewId = CrewId.Create();
        var input = new CrewInput("ctx", new Dictionary<string, object> { ["name"] = "Alice", ["age"] = "30" });
        using var memory = new TestMemoryScope();

        var ctx = SimpleExecutionContext.Create(crewId, input, memory);

        Assert.True(ctx.Variables.ContainsKey("name"));
        Assert.Equal("Alice", ctx.Variables["name"]);
    }

    [Fact]
    public void Create_ShouldUseEmptyVariables_WhenInputHasNullVariables()
    {
        var crewId = CrewId.Create();
        var input = new CrewInput("ctx", new Dictionary<string, object>());
        using var memory = new TestMemoryScope();

        var ctx = SimpleExecutionContext.Create(crewId, input, memory);

        Assert.NotNull(ctx.Variables);
    }

    [Fact]
    public void Create_ShouldSetMemoryScope()
    {
        var crewId = CrewId.Create();
        var input = new CrewInput(null, new Dictionary<string, object>());
        using var memory = new TestMemoryScope();

        var ctx = SimpleExecutionContext.Create(crewId, input, memory);

        Assert.Same(memory, ctx.Memory);
    }

    [Fact]
    public void Create_ShouldHaveEmptyPreviousOutputs()
    {
        var crewId = CrewId.Create();
        var input = new CrewInput(null, new Dictionary<string, object>());
        using var memory = new TestMemoryScope();

        var ctx = SimpleExecutionContext.Create(crewId, input, memory);

        Assert.Empty(ctx.PreviousOutputs);
    }

    [Fact]
    public void Create_ShouldUseCancellationTokenNone()
    {
        var crewId = CrewId.Create();
        var input = new CrewInput(null, new Dictionary<string, object>());
        using var memory = new TestMemoryScope();

        var ctx = SimpleExecutionContext.Create(crewId, input, memory);

        Assert.Equal(CancellationToken.None, ctx.CancellationToken);
    }
}
