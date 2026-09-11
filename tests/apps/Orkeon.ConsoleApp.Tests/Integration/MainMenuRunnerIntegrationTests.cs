using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Tests.Console;
using Orkeon.ConsoleApp.Commands;
using Orkeon.ConsoleApp.Commands.Agent;
using Orkeon.ConsoleApp.Commands.Crew;
using Orkeon.ConsoleApp.Commands.Qa;
using Orkeon.ConsoleApp.Commands.Task;
using Orkeon.ConsoleApp.Registries;
using Orkeon.ConsoleApp.Runners;
using Orkeon.ConsoleApp.Services;
using Orkeon.ConsoleApp.Tests.Fakes;
using Orkeon.Domain.FileSystem;

namespace Orkeon.ConsoleApp.Tests.Integration;

public sealed class MainMenuRunnerIntegrationTests
{
    private static (MainMenuRunner runner, TestConsoleAdapter console, IServiceProvider sp, FakeCrewOrchestrationService orch) Build()
    {
        var console = new TestConsoleAdapter();
        var fs = new FakeFileSystemService();
        fs.AddFile("/tmp/test.yaml", "name: x");
        var orch = new FakeCrewOrchestrationService();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddFilter(_ => false));
        services.AddSingleton<IConsoleAdapter>(console);
        services.AddSingleton(new ConsoleInputService(console));
        services.AddSingleton<IFileSystemService>(fs);
        services.AddSingleton<ICrewFactory>(new FakeCrewFactory());
        services.AddSingleton<ICrewOrchestrationService>(orch);
        services.AddSingleton<AgentManagementService>();
        services.AddSingleton<CrewManagementService>();
        services.AddSingleton<TaskManagementService>();

        services.AddOrkeonCli();
        services.AddSingleton<AskQuestionCommand>();
        services.AddSingleton<QaCommandRegistry>(sp =>
            new QaCommandRegistry(sp.GetRequiredService<AskQuestionCommand>()));
        services.AddSingleton<QaRunner>();

        services.AddSingleton<IMainMenuCommand, ListAgentsCommand>();
        services.AddSingleton<IMainMenuCommand, CreateAgentCommand>();
        services.AddSingleton<IMainMenuCommand, DeleteAgentCommand>();
        services.AddSingleton<IMainMenuCommand, ListCrewsCommand>();
        services.AddSingleton<IMainMenuCommand, CreateCrewCommand>();
        services.AddSingleton<IMainMenuCommand, AddAgentToCrewCommand>();
        services.AddSingleton<IMainMenuCommand, DeleteCrewCommand>();
        services.AddSingleton<IMainMenuCommand, RunCrewCommand>();
        services.AddSingleton<IMainMenuCommand, ListTasksCommand>();
        services.AddSingleton<IMainMenuCommand, CreateTaskCommand>();
        services.AddSingleton<IMainMenuCommand, AssignTaskCommand>();
        services.AddSingleton<IMainMenuCommand, DeleteTaskCommand>();
        services.AddSingleton<IMainMenuCommand, DemoCommand>();
        services.AddSingleton<IMainMenuCommand, EnterQaModeCommand>();

        services.AddSingleton<MainMenuCommandRegistry>();
        services.AddSingleton<MainMenuRunner>();

        var sp2 = services.BuildServiceProvider();
        var runner = sp2.GetRequiredService<MainMenuRunner>();
        return (runner, console, sp2, orch);
    }

    private static async System.Threading.Tasks.Task DriveAsync(MainMenuRunner runner, TestConsoleAdapter console, params string?[] inputs)
    {
        var run = System.Threading.Tasks.Task.Run(() => runner.RunAsync(CancellationToken.None));
        foreach (var input in inputs)
        {
            var blocked = console.WaitUntilBlockedAsync(TimeSpan.FromSeconds(5));
            var any = await System.Threading.Tasks.Task.WhenAny(blocked, run).ConfigureAwait(false);
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
    public async System.Threading.Tasks.Task Run_displays_main_menu_banner_then_prompt()
    {
        var (runner, console, _, _) = Build();
        await DriveAsync(runner, console, "exit");
        var output = console.GetOutput();
        Assert.Contains("Orkeon Console Application", output);
        Assert.Contains("orkeon> ", output);
    }

    [Fact]
    public async System.Threading.Tasks.Task Run_help_lists_all_command_groups()
    {
        var (runner, console, _, _) = Build();
        await DriveAsync(runner, console, "help", "exit");
        var output = console.GetOutput();
        Assert.Contains("[Default]", output);
        Assert.Contains("[agent]", output);
        Assert.Contains("[crew]", output);
        Assert.Contains("[task]", output);
        Assert.Contains("[(no group)]", output);
    }

    [Fact]
    public async System.Threading.Tasks.Task Run_exit_terminates_loop()
    {
        var (runner, console, _, _) = Build();
        await DriveAsync(runner, console, "exit");
        Assert.Contains("Goodbye!", console.GetOutput());
    }

    [Fact]
    public async System.Threading.Tasks.Task Run_unknown_command_writes_error_and_continues()
    {
        var (runner, console, _, _) = Build();
        await DriveAsync(runner, console, "nonsense", "exit");
        Assert.Contains("Unknown command: 'nonsense'", console.GetOutput());
    }

    [Fact]
    public async System.Threading.Tasks.Task Run_agent_list_invokes_AgentManagementService()
    {
        var (runner, console, _, _) = Build();
        await DriveAsync(runner, console, "agent list", "exit");
        Assert.Contains("List of Agents", console.GetOutput());
    }

    [Fact]
    public async System.Threading.Tasks.Task Run_qa_command_enters_QaRunner_then_returns_on_qa_exit()
    {
        var (runner, console, _, _) = Build();
        // qa → enter QA → send YAML path → exit QA → exit main
        await DriveAsync(runner, console, "qa", "/tmp/test.yaml", "exit", "exit");
        var output = console.GetOutput();
        Assert.Contains("Interactive Q&A Session", output);
        Assert.Contains("Returned from Q&A mode.", output);
    }

    [Fact]
    public async System.Threading.Tasks.Task Run_help_inside_QaRunner_shows_qa_specific_help()
    {
        var (runner, console, _, _) = Build();
        await DriveAsync(runner, console, "qa", "/tmp/test.yaml", "help", "exit", "exit");
        var output = console.GetOutput();
        // QA help shows the [Default action] block from fallback
        Assert.Contains("[Default action]", output);
    }
}
