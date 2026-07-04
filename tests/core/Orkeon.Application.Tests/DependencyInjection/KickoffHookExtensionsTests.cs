using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Callback;
using Orkeon.Application.DependencyInjection;
using Orkeon.Domain.SharedKernel.ValueObjects;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Application.Tests.DependencyInjection;

/// <summary>
/// R4.9 (MORT-004) — kickoff hooks are an opt-in subsystem: not registered by
/// <c>AddOrkeonApplication()</c>, explicitly activated via <c>AddOrkeonKickoffHooks()</c>.
/// See <c>docs/reference/opt-in-subsystems.md</c>.
/// </summary>
public class KickoffHookExtensionsTests
{
    private static readonly string[] FirstSecondOrder = ["first", "second"];

    private static DomainCrew CreateTestCrew() =>
        DomainCrew.Create(goal: "Test Crew Goal", processType: ProcessType.Sequential);

    [Fact]
    public void ShouldNotRegisterHookRunner_WhenAddOrkeonApplication()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOrkeonApplication();

        // Assert — the hook runner is opt-in, never part of the defaults.
        Assert.DoesNotContain(services, sd =>
            sd.ServiceType == typeof(ICrewKickoffHookRunner));
    }

    [Fact]
    public void ShouldResolveHookRunner_WhenAddOrkeonKickoffHooks()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOrkeonKickoffHooks();
        using var provider = services.BuildServiceProvider();

        // Assert — resolvable with zero hooks registered (empty collections).
        Assert.NotNull(provider.GetRequiredService<ICrewKickoffHookRunner>());
    }

    [Fact]
    public void ShouldNotDuplicateRunner_WhenAddOrkeonKickoffHooksCalledTwice()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOrkeonKickoffHooks();
        services.AddOrkeonKickoffHooks();

        // Assert — TryAdd keeps the registration idempotent.
        Assert.Single(services, sd => sd.ServiceType == typeof(ICrewKickoffHookRunner));
    }

    [Fact]
    public void ShouldRegisterHookAndRunner_WhenAddBeforeKickoffHook()
    {
        // Arrange
        var services = new ServiceCollection();
        var hook = new BeforeKickoffHook { Name = "audit-before" };

        // Act
        services.AddBeforeKickoffHook(hook);
        using var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetRequiredService<ICrewKickoffHookRunner>());
        Assert.Contains(hook, provider.GetServices<BeforeKickoffHook>());
    }

    [Fact]
    public void ShouldRegisterHookAndRunner_WhenAddAfterKickoffHook()
    {
        // Arrange
        var services = new ServiceCollection();
        var hook = new AfterKickoffHook { Name = "audit-after" };

        // Act
        services.AddAfterKickoffHook(hook);
        using var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetRequiredService<ICrewKickoffHookRunner>());
        Assert.Contains(hook, provider.GetServices<AfterKickoffHook>());
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldRunHooksInPriorityOrder_WhenRunBeforeKickoffAsync()
    {
        // Arrange
        var executionOrder = new List<string>();
        var services = new ServiceCollection();
        services.AddBeforeKickoffHook(new BeforeKickoffHook
        {
            Name = "second",
            Priority = 10,
            Execute = _ =>
            {
                executionOrder.Add("second");
                return System.Threading.Tasks.Task.CompletedTask;
            },
        });
        services.AddBeforeKickoffHook(new BeforeKickoffHook
        {
            Name = "first",
            Priority = 1,
            Execute = _ =>
            {
                executionOrder.Add("first");
                return System.Threading.Tasks.Task.CompletedTask;
            },
        });
        using var provider = services.BuildServiceProvider();
        var runner = provider.GetRequiredService<ICrewKickoffHookRunner>();

        // Act
        await runner.RunBeforeKickoffAsync(CreateTestCrew(), TestContext.Current.CancellationToken);

        // Assert — lower priority value executes first.
        Assert.Equal(FirstSecondOrder, executionOrder);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldContinue_WhenHookThrowsWithContinueOnError()
    {
        // Arrange
        var secondHookRan = false;
        var services = new ServiceCollection();
        services.AddBeforeKickoffHook(new BeforeKickoffHook
        {
            Name = "failing",
            Priority = 1,
            ContinueOnError = true,
            Execute = _ => throw new InvalidOperationException("boom"),
        });
        services.AddBeforeKickoffHook(new BeforeKickoffHook
        {
            Name = "next",
            Priority = 2,
            Execute = _ =>
            {
                secondHookRan = true;
                return System.Threading.Tasks.Task.CompletedTask;
            },
        });
        using var provider = services.BuildServiceProvider();
        var runner = provider.GetRequiredService<ICrewKickoffHookRunner>();

        // Act — must not throw.
        await runner.RunBeforeKickoffAsync(CreateTestCrew(), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(secondHookRan);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldRethrow_WhenHookThrowsWithoutContinueOnError()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddBeforeKickoffHook(new BeforeKickoffHook
        {
            Name = "failing-hard",
            ContinueOnError = false,
            Execute = _ => throw new InvalidOperationException("boom"),
        });
        using var provider = services.BuildServiceProvider();
        var runner = provider.GetRequiredService<ICrewKickoffHookRunner>();

        // Act + Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunBeforeKickoffAsync(CreateTestCrew(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldPassCrewAndResult_WhenRunAfterKickoffAsync()
    {
        // Arrange
        DomainCrew? observedCrew = null;
        object? observedResult = null;
        var services = new ServiceCollection();
        services.AddAfterKickoffHook(new AfterKickoffHook
        {
            Name = "observer",
            Execute = (crew, result) =>
            {
                observedCrew = crew;
                observedResult = result;
                return System.Threading.Tasks.Task.CompletedTask;
            },
        });
        using var provider = services.BuildServiceProvider();
        var runner = provider.GetRequiredService<ICrewKickoffHookRunner>();
        var testCrew = CreateTestCrew();

        // Act
        await runner.RunAfterKickoffAsync(testCrew, "final output", TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(testCrew, observedCrew);
        Assert.Equal("final output", observedResult);
    }
}
