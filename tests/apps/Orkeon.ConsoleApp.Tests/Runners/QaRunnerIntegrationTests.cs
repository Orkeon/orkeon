using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Tests.Console;
using Orkeon.ConsoleApp.Commands.Qa;
using Orkeon.ConsoleApp.Registries;
using Orkeon.ConsoleApp.Runners;
using Orkeon.ConsoleApp.Tests.Fakes;
using Orkeon.Domain.FileSystem;

namespace Orkeon.ConsoleApp.Tests.Runners;

public sealed class QaRunnerIntegrationTests
{
    private static (QaRunner runner, TestConsoleAdapter console, FakeFileSystemService fs, FakeCrewFactory factory, FakeCrewOrchestrationService orch) Build()
    {
        var console = new TestConsoleAdapter();
        var fs = new FakeFileSystemService();
        fs.AddFile("/some/crew.yaml", "name: x");
        var factory = new FakeCrewFactory();
        var orch = new FakeCrewOrchestrationService();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddFilter(_ => false));
        services.AddSingleton<IConsoleAdapter>(console);
        services.AddSingleton(new ConsoleInputService(console));
        services.AddSingleton<IFileSystemService>(fs);
        services.AddSingleton<ICrewFactory>(factory);
        services.AddSingleton<ICrewOrchestrationService>(orch);
        services.AddOrkeonCli();
        services.AddSingleton<AskQuestionCommand>();
        services.AddSingleton<QaCommandRegistry>(sp =>
            new QaCommandRegistry(sp.GetRequiredService<AskQuestionCommand>()));
        services.AddSingleton<QaRunner>();

        var sp = services.BuildServiceProvider();
        var runner = sp.GetRequiredService<QaRunner>();
        return (runner, console, fs, factory, orch);
    }

    private static async Task DriveAsync(QaRunner runner, TestConsoleAdapter console, params string?[] inputs)
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
        if (!await FinishedWithinAsync(run, TimeSpan.FromSeconds(5)).ConfigureAwait(false)) console.Complete();
        await run.ConfigureAwait(false);
    }

    // WaitAsync rather than WhenAny + Delay: a Delay that lost the race keeps its timer
    // alive until it fires (CA2027, .NET 11 SDK analyzers).
    private static async System.Threading.Tasks.Task<bool> FinishedWithinAsync(System.Threading.Tasks.Task task, TimeSpan timeout)
    {
        try
        {
            await task.WaitAsync(timeout).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    [Fact]
    public async Task Run_displays_banner_then_prompts_for_yaml_path()
    {
        var (runner, console, _, _, _) = Build();

        // Send the YAML path then exit
        await DriveAsync(runner, console, "/some/crew.yaml", "exit");

        var output = console.GetOutput();
        Assert.Contains("Interactive Q&A Session", output);
        Assert.Contains("Path to crew config.yaml", output);
    }

    [Fact]
    public async Task Run_with_empty_yaml_path_uses_builtin_config()
    {
        var (runner, console, fs, _, _) = Build();

        await DriveAsync(runner, console, "", "exit");

        Assert.Equal("/tmp/interactive-qa", runner.CrewConfigPath?.Substring(0, "/tmp/interactive-qa".Length));
        Assert.Contains("Using built-in interactive Q&A crew.", console.GetOutput());
    }

    [Fact]
    public async Task Run_with_invalid_yaml_path_throws_FileNotFound()
    {
        var (runner, console, _, _, _) = Build();

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => DriveAsync(runner, console, "/missing.yaml"));
    }

    [Fact]
    public async Task Run_exit_command_terminates_after_OnStartAsync()
    {
        var (runner, console, _, _, _) = Build();

        await DriveAsync(runner, console, "/some/crew.yaml", "exit");

        Assert.Contains("Goodbye!", console.GetOutput());
    }

    [Fact]
    public async Task Run_help_command_lists_default_commands_and_fallback_action()
    {
        var (runner, console, _, _, _) = Build();

        await DriveAsync(runner, console, "/some/crew.yaml", "help", "exit");

        var output = console.GetOutput();
        Assert.Contains("[Default]", output);
        Assert.Contains("[Default action]", output);
    }

    [Fact]
    public async Task Run_clear_command_clears_console()
    {
        var (runner, console, _, _, _) = Build();

        await DriveAsync(runner, console, "/some/crew.yaml", "clear", "exit");

        Assert.True(console.ClearCount >= 1);
    }

    [Fact]
    public async Task Run_question_invokes_fallback_AskQuestionCommand_with_question_text()
    {
        var (runner, console, _, _, orch) = Build();

        await DriveAsync(runner, console, "/some/crew.yaml", "What is 2+2?", "exit");

        Assert.Single(orch.Calls);
        Assert.Equal("What is 2+2?", orch.Calls[0].Input.InitialContext);
    }

    [Fact]
    public async Task Run_multiple_questions_processed_sequentially()
    {
        var (runner, console, _, _, orch) = Build();

        await DriveAsync(runner, console, "/some/crew.yaml", "Q1?", "Q2?", "Q3?", "exit");

        Assert.Equal(3, orch.Calls.Count);
        Assert.Equal("Q1?", orch.Calls[0].Input.InitialContext);
        Assert.Equal("Q3?", orch.Calls[2].Input.InitialContext);
    }

    [Fact]
    public async Task Run_question_failure_continues_loop_with_error_message()
    {
        var (runner, console, _, _, orch) = Build();
        orch.ThrowOnKickoff = new InvalidOperationException("crew exploded");

        await DriveAsync(runner, console, "/some/crew.yaml", "what?", "exit");

        Assert.Contains("crew exploded", console.GetOutput());
        // Loop continued past failure → exit was reached
        Assert.Contains("Goodbye!", console.GetOutput());
    }

    [Fact]
    public async Task Run_OnExit_called_when_user_exits()
    {
        var (runner, console, _, _, _) = Build();

        await DriveAsync(runner, console, "/some/crew.yaml", "exit");

        // The exit message ("Goodbye!") proves OnExit ran (after which the runner returns)
        Assert.Contains("Goodbye!", console.GetOutput());
    }
}
