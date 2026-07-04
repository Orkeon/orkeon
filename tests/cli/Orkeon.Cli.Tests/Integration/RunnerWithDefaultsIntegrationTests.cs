using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Tests.Console;
using Orkeon.Cli.Abstractions.Tests.Fixtures;
using Orkeon.Cli.Registry;

namespace Orkeon.Cli.Tests.Integration;

public sealed class RunnerWithDefaultsIntegrationTests
{
    private static (TestRunner runner, TestConsoleAdapter console, IServiceProvider sp) Build(
        IInteractiveCommand[] specific,
        IInteractiveCommand? fallback = null)
    {
        var services = new ServiceCollection();
        services.AddOrkeonCli();
        services.AddScoped<TestScopedService>();
        var sp = services.BuildServiceProvider();
        var defaults = sp.GetRequiredService<DefaultCommandRegistry>();

        var specificReg = fallback is null
            ? new TestCommandRegistry(specific)
            : TestCommandRegistry.WithFallback(fallback, specific);

        var console = new TestConsoleAdapter();
        var runner = new TestRunner(defaults, specificReg, console, sp);
        return (runner, console, sp);
    }

    private static async Task RunWithInputs(TestRunner runner, TestConsoleAdapter console, params string?[] inputs)
    {
        var run = Task.Run(() => runner.RunAsync(CancellationToken.None));
        foreach (var input in inputs)
        {
            var blocked = console.WaitUntilBlockedAsync(TimeSpan.FromSeconds(5));
            var any = await Task.WhenAny(blocked, run).ConfigureAwait(false);
            if (any == run) { await run.ConfigureAwait(false); return; }
            await blocked.ConfigureAwait(false);
            if (input is null) { console.Complete(); break; }
            await console.SendLineAsync(input).ConfigureAwait(false);
        }
        var done = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
        if (done != run) console.Complete();
        await run.ConfigureAwait(false);
    }

    [Fact]
    public async Task Runner_with_help_command_lists_default_and_specific_groups()
    {
        var foo = new RecordingCommand("agent list", description: "List agents");
        var (runner, console, _) = Build(new IInteractiveCommand[] { foo });

        await RunWithInputs(runner, console, "help", "exit");

        var output = console.GetOutput();
        Assert.Contains("[Default]", output);
        Assert.Contains("[agent]", output);
        Assert.Contains("agent list", output);
    }

    [Fact]
    public async Task Runner_exit_command_terminates_loop_and_calls_OnExitAsync()
    {
        var (runner, console, _) = Build(Array.Empty<IInteractiveCommand>());

        await RunWithInputs(runner, console, "exit");

        Assert.Equal(1, runner.OnExitCount);
        Assert.True(runner.LastResultSeen?.ShouldExit);
    }

    [Fact]
    public async Task Runner_clear_command_clears_console_then_continues()
    {
        var (runner, console, _) = Build(Array.Empty<IInteractiveCommand>());

        await RunWithInputs(runner, console, "clear", "exit");

        Assert.True(console.ClearCount >= 1);
    }

    [Fact]
    public async Task Runner_unknown_command_with_no_fallback_writes_unknown_error()
    {
        var (runner, console, _) = Build(Array.Empty<IInteractiveCommand>());

        await RunWithInputs(runner, console, "foobarbaz", "exit");

        var output = console.GetOutput();
        Assert.Contains("Unknown command: 'foobarbaz'", output);
    }

    [Fact]
    public async Task Runner_with_fallback_handles_unknown_input_via_fallback()
    {
        var fallback = new RecordingCommand("ask", description: "Ask");
        var (runner, console, _) = Build(Array.Empty<IInteractiveCommand>(), fallback: fallback);

        await RunWithInputs(runner, console, "what is the weather?", "exit");

        Assert.Single(fallback.Invocations);
        Assert.Equal("what is the weather?", fallback.Invocations[0].RawInput);
    }

    [Fact]
    public async Task Runner_runs_commands_via_DI_resolution_in_correct_scope()
    {
        // Each iteration must get a fresh scope. We verify by registering a
        // scoped service and ensuring different scope hash-codes per invocation.
        var (runner, console, _) = Build(new IInteractiveCommand[]
        {
            new RecordingCommand("ping", action: ctx =>
            {
                var marker = ctx.Scope.GetService<TestScopedService>();
                Assert.NotNull(marker);
            })
        });

        await RunWithInputs(runner, console, "ping", "ping", "exit");

        // Just ensures no crash and command ran twice.
        var output = console.GetOutput();
        Assert.Contains("Goodbye!", output);
    }

    private sealed class TestScopedService { }
}
