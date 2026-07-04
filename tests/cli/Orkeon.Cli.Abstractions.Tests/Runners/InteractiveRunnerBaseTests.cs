using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Tests.Console;
using Orkeon.Cli.Abstractions.Tests.Fixtures;

namespace Orkeon.Cli.Abstractions.Tests.Runners;

public sealed class InteractiveRunnerBaseTests
{
    private static (TestRunner runner, TestConsoleAdapter console, RecordingCommand[] specific, RecordingCommand[] defaults) Build(
        IInteractiveCommand[] specific,
        IInteractiveCommand[] defaults,
        IInteractiveCommand? fallback = null)
    {
        var console = new TestConsoleAdapter();
        var sp = new ServiceCollection().BuildServiceProvider();
        var specificReg = fallback is null
            ? new TestCommandRegistry(specific)
            : TestCommandRegistry.WithFallback(fallback, specific);
        var runner = new TestRunner(
            new TestCommandRegistry(defaults),
            specificReg,
            console,
            sp);
        return (runner, console,
            specific.OfType<RecordingCommand>().ToArray(),
            defaults.OfType<RecordingCommand>().ToArray());
    }

    private static async Task RunWithInputs(TestRunner runner, TestConsoleAdapter console, params string?[] inputs)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(console);
        if (inputs is null) inputs = new string?[] { null };
        var run = Task.Run(() => runner.RunAsync(CancellationToken.None));
        foreach (var input in inputs)
        {
            var blocked = console.WaitUntilBlockedAsync(TimeSpan.FromSeconds(5));
            var completed = await Task.WhenAny(blocked, run).ConfigureAwait(false);
            if (completed == run) { await run.ConfigureAwait(false); return; }
            await blocked.ConfigureAwait(false);
            if (input is null)
            {
                console.Complete();
                break;
            }
            await console.SendLineAsync(input).ConfigureAwait(false);
        }
        // Ensure the runner exits eventually
        var done = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
        if (done != run)
        {
            console.Complete();
            await run.ConfigureAwait(false);
        }
        else
        {
            await run.ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task RunAsync_displays_banner_then_prompt()
    {
        var (runner, console, _, _) = Build(Array.Empty<IInteractiveCommand>(), Array.Empty<IInteractiveCommand>());
        await RunWithInputs(runner, console, (string?)null);

        var output = console.GetOutput();
        Assert.Contains("TEST", output);
        Assert.Contains("test> ", output);
    }

    [Fact]
    public async Task RunAsync_breaks_on_null_input()
    {
        var (runner, console, _, _) = Build(Array.Empty<IInteractiveCommand>(), Array.Empty<IInteractiveCommand>());
        await RunWithInputs(runner, console, (string?)null);

        Assert.Equal(1, runner.OnStartCount);
        Assert.Equal(1, runner.OnExitCount);
    }

    [Fact]
    public async Task RunAsync_ignores_empty_lines_and_reprompts()
    {
        var exit = new RecordingCommand("exit", result: _ => CommandResult.Exit());
        var (runner, console, _, _) = Build(
            specific: Array.Empty<IInteractiveCommand>(),
            defaults: new IInteractiveCommand[] { exit });

        await RunWithInputs(runner, console, "", "   ", "exit");

        Assert.Single(exit.Invocations);
    }

    [Fact]
    public async Task RunAsync_resolves_specific_command_first()
    {
        var defaultHelp = new RecordingCommand("help", description: "default help");
        var specificHelp = new RecordingCommand("help", result: _ => CommandResult.Exit("specific won"));
        var (runner, console, _, _) = Build(
            specific: new IInteractiveCommand[] { specificHelp },
            defaults: new IInteractiveCommand[] { defaultHelp });

        await RunWithInputs(runner, console, "help");

        Assert.Single(specificHelp.Invocations);
        Assert.Empty(defaultHelp.Invocations);
    }

    [Fact]
    public async Task RunAsync_falls_back_to_default_command()
    {
        var defaultExit = new RecordingCommand("exit", result: _ => CommandResult.Exit());
        var (runner, console, _, _) = Build(
            specific: Array.Empty<IInteractiveCommand>(),
            defaults: new IInteractiveCommand[] { defaultExit });

        await RunWithInputs(runner, console, "exit");

        Assert.Single(defaultExit.Invocations);
    }

    [Fact]
    public async Task RunAsync_invokes_fallback_when_no_match()
    {
        var fallback = new RecordingCommand("fb", result: _ => CommandResult.Exit());
        var (runner, console, _, _) = Build(
            specific: Array.Empty<IInteractiveCommand>(),
            defaults: Array.Empty<IInteractiveCommand>(),
            fallback: fallback);

        await RunWithInputs(runner, console, "what is the weather?");

        Assert.Single(fallback.Invocations);
        Assert.Equal("what is the weather?", fallback.Invocations[0].RawInput);
    }

    [Fact]
    public async Task RunAsync_calls_OnUnknownCommand_when_no_fallback()
    {
        var exit = new RecordingCommand("exit", result: _ => CommandResult.Exit());
        var (runner, console, _, _) = Build(
            specific: Array.Empty<IInteractiveCommand>(),
            defaults: new IInteractiveCommand[] { exit });

        await RunWithInputs(runner, console, "foobar", "exit");

        var output = console.GetOutput();
        Assert.Contains("Unknown command: 'foobar'", output);
    }

    [Fact]
    public async Task RunAsync_breaks_when_command_returns_ShouldExit_true()
    {
        var exit = new RecordingCommand("bye", result: _ => CommandResult.Exit("Goodbye!"));
        var (runner, console, _, _) = Build(
            specific: new IInteractiveCommand[] { exit },
            defaults: Array.Empty<IInteractiveCommand>());

        await RunWithInputs(runner, console, "bye");

        Assert.True(runner.LastResultSeen?.ShouldExit);
        Assert.Contains("Goodbye!", console.GetOutput());
    }

    [Fact]
    public async Task RunAsync_continues_after_command_exception()
    {
        var crash = new RecordingCommand("crash", action: _ => throw new InvalidOperationException("boom"));
        var exit = new RecordingCommand("exit", result: _ => CommandResult.Exit());
        var (runner, console, _, _) = Build(
            specific: new IInteractiveCommand[] { crash, exit },
            defaults: Array.Empty<IInteractiveCommand>());

        await RunWithInputs(runner, console, "crash", "exit");

        var output = console.GetOutput();
        Assert.Contains("Error: boom", output);
        Assert.Equal(1, runner.OnExitCount);
        Assert.Single(exit.Invocations);
    }

    // ── Slash-only grammar (capabilities 1 & 5) ───────────────────────────────────────────

    [Fact]
    public async Task SlashMode_resolves_slash_prefixed_command()
    {
        var exit = new RecordingCommand("exit", result: _ => CommandResult.Exit());
        var (runner, console, _, _) = Build(
            specific: Array.Empty<IInteractiveCommand>(),
            defaults: new IInteractiveCommand[] { exit });
        runner.SlashOnly = true;

        await RunWithInputs(runner, console, "/exit");

        Assert.Single(exit.Invocations);
    }

    [Fact]
    public async Task SlashMode_routes_free_text_to_fallback()
    {
        var fallback = new RecordingCommand("assistant", result: _ => CommandResult.Exit());
        var run = new RecordingCommand("run");
        var (runner, console, _, _) = Build(
            specific: new IInteractiveCommand[] { run },
            defaults: Array.Empty<IInteractiveCommand>(),
            fallback: fallback);
        runner.SlashOnly = true;

        await RunWithInputs(runner, console, "fix the failing test");

        Assert.Single(fallback.Invocations);
        Assert.Equal("fix the failing test", fallback.Invocations[0].RawInput);
        Assert.Empty(run.Invocations);
    }

    [Fact]
    public async Task SlashMode_routes_at_file_line_to_fallback_not_command()
    {
        var fallback = new RecordingCommand("assistant", result: _ => CommandResult.Exit());
        var (runner, console, _, _) = Build(
            specific: Array.Empty<IInteractiveCommand>(),
            defaults: Array.Empty<IInteractiveCommand>(),
            fallback: fallback);
        runner.SlashOnly = true;

        await RunWithInputs(runner, console, "@src/Program.cs explain this");

        Assert.Single(fallback.Invocations);
        Assert.Equal("@src/Program.cs explain this", fallback.Invocations[0].RawInput);
    }

    [Fact]
    public async Task SlashMode_unknown_slash_command_does_not_hit_fallback()
    {
        var fallback = new RecordingCommand("assistant", result: _ => CommandResult.Continue());
        var exit = new RecordingCommand("exit", result: _ => CommandResult.Exit());
        var (runner, console, _, _) = Build(
            specific: Array.Empty<IInteractiveCommand>(),
            defaults: new IInteractiveCommand[] { exit },
            fallback: fallback);
        runner.SlashOnly = true;

        await RunWithInputs(runner, console, "/nope", "/exit");

        Assert.Empty(fallback.Invocations);
        Assert.Contains("Unknown command: '/nope'", console.GetOutput());
    }

    [Fact]
    public async Task SlashMode_bare_command_name_is_treated_as_free_text()
    {
        // Without a leading '/', even a real command name is free text → fallback (cap 5).
        var fallback = new RecordingCommand("assistant", result: _ => CommandResult.Exit());
        var exit = new RecordingCommand("exit", result: _ => CommandResult.Exit());
        var (runner, console, _, _) = Build(
            specific: Array.Empty<IInteractiveCommand>(),
            defaults: new IInteractiveCommand[] { exit },
            fallback: fallback);
        runner.SlashOnly = true;

        await RunWithInputs(runner, console, "exit");

        Assert.Single(fallback.Invocations);
        Assert.Empty(exit.Invocations);
    }

    [Fact]
    public async Task RunAsync_pushes_then_pops_RunnerContext()
    {
        var (runner, console, _, _) = Build(Array.Empty<IInteractiveCommand>(), Array.Empty<IInteractiveCommand>());
        await RunWithInputs(runner, console, (string?)null);

        Assert.NotNull(runner.RegistrySnapshotDuringRun);
        Assert.Same(runner.SpecificForTest, runner.RegistrySnapshotDuringRun);
        Assert.Null(Orkeon.Cli.Abstractions.Runners.RunnerContext.Current);
    }
}
