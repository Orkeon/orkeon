using Orkeon.Application.Context;

namespace Orkeon.Application.Tests.Context;

public class NullMemoryScopeTests
{
    [Fact]
    public void Instance_ShouldBeSingleton()
    {
        Assert.Same(NullMemoryScope.Instance, NullMemoryScope.Instance);
    }

    [Fact]
    public void AgentId_ShouldReturnNone()
    {
        Assert.Equal("none", NullMemoryScope.Instance.AgentId);
    }

    [Fact]
    public void ScopeId_ShouldReturnNullScope()
    {
        Assert.Equal("null-scope", NullMemoryScope.Instance.ScopeId);
    }

    [Fact]
    public async System.Threading.Tasks.Task ExecuteInScopeAsync_WithReturn_ShouldInvokeAndReturnResult()
    {
        var result = await NullMemoryScope.Instance.ExecuteInScopeAsync(() =>
            System.Threading.Tasks.Task.FromResult(42));

        Assert.Equal(42, result);
    }

    [Fact]
    public async System.Threading.Tasks.Task ExecuteInScopeAsync_WithReturn_ShouldInvokeOperationDirectly()
    {
        var executed = false;
        await NullMemoryScope.Instance.ExecuteInScopeAsync(async () =>
        {
            executed = true;
            await System.Threading.Tasks.Task.CompletedTask;
            return true;
        });

        Assert.True(executed);
    }

    [Fact]
    public async System.Threading.Tasks.Task ExecuteInScopeAsync_Void_ShouldInvokeOperation()
    {
        var executed = false;
        await NullMemoryScope.Instance.ExecuteInScopeAsync(() =>
        {
            executed = true;
            return System.Threading.Tasks.Task.CompletedTask;
        });

        Assert.True(executed);
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        using var scope = new NullMemoryScope();
        var ex = Record.Exception(() => scope.Dispose());

        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var scope = new NullMemoryScope();
        scope.Dispose();
        var ex = Record.Exception(() => scope.Dispose());

        Assert.Null(ex);
    }
}
