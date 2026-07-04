using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Tests.Console;
using Orkeon.Cli.Commands;
using Orkeon.Cli.Registry;
using Orkeon.ConsoleApp.Commands.Qa;
using Orkeon.ConsoleApp.Registries;
using Orkeon.ConsoleApp.Runners;
using Orkeon.ConsoleApp.Tests.Fakes;
using Orkeon.Domain.FileSystem;

namespace Orkeon.ConsoleApp.Tests.Commands.Qa;

public sealed class AskQuestionCommandTests
{
    private static (AskQuestionCommand cmd, CommandContext ctx, TestConsoleAdapter console, FakeCrewFactory factory, FakeCrewOrchestrationService orch, QaRunner runner) Build(string? configPath = "/some/path.yaml")
    {
        var console = new TestConsoleAdapter();
        var fs = new FakeFileSystemService();
        var factory = new FakeCrewFactory();
        var orch = new FakeCrewOrchestrationService();

        var defaults = new DefaultCommandRegistry(new HelpCommand(), new ExitCommand(), new ClearCommand());
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddFilter(_ => false));
        services.AddSingleton<IConsoleAdapter>(console);
        services.AddSingleton(fs);
        services.AddSingleton<IFileSystemService>(fs);
        services.AddSingleton(new ConsoleInputService(console));
        services.AddSingleton(defaults);
        services.AddSingleton<QaCommandRegistry>(_ => new QaCommandRegistry());
        services.AddSingleton<ICrewFactory>(factory);
        services.AddSingleton<ICrewOrchestrationService>(orch);
        services.AddSingleton<QaRunner>();
        var sp = services.BuildServiceProvider();

        var runner = sp.GetRequiredService<QaRunner>();
        if (configPath is not null)
        {
            typeof(QaRunner).GetProperty(nameof(QaRunner.CrewConfigPath))!
                .SetValue(runner, configPath);
        }

        var cmd = new AskQuestionCommand(NullLogger<AskQuestionCommand>.Instance);

        var ctx = new CommandContext
        {
            RawInput = "Hello?",
            Args = new[] { "Hello?" },
            Console = console,
            Scope = sp
        };

        return (cmd, ctx, console, factory, orch, runner);
    }

    [Fact]
    public async Task Execute_throws_when_runner_not_started()
    {
        var (cmd, ctx, _, _, _, _) = Build(configPath: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => cmd.ExecuteAsync(ctx, CancellationToken.None));
    }

    [Fact]
    public async Task Execute_creates_crew_from_path_and_kicks_off()
    {
        var (cmd, ctx, _, factory, orch, _) = Build();

        await cmd.ExecuteAsync(ctx, CancellationToken.None);

        Assert.Single(factory.PathsLoaded);
        Assert.Equal("/some/path.yaml", factory.PathsLoaded[0]);
        Assert.Single(orch.Calls);
    }

    [Fact]
    public async Task Execute_displays_final_output_and_metrics()
    {
        var (cmd, ctx, console, _, _, _) = Build();

        await cmd.ExecuteAsync(ctx, CancellationToken.None);

        var output = console.GetOutput();
        Assert.Contains("[Answer]", output);
        Assert.Contains("answer to:", output);
        Assert.Contains("0.5s", output);
        Assert.Contains("30 tokens", output);
    }

    [Fact]
    public async Task Execute_returns_Continue_on_success()
    {
        var (cmd, ctx, _, _, _, _) = Build();

        var result = await cmd.ExecuteAsync(ctx, CancellationToken.None);

        Assert.False(result.ShouldExit);
    }

    [Fact]
    public async Task Execute_returns_Continue_with_error_message_on_exception()
    {
        var (cmd, ctx, _, _, orch, _) = Build();
        orch.ThrowOnKickoff = new InvalidOperationException("crew failed");

        var result = await cmd.ExecuteAsync(ctx, CancellationToken.None);

        Assert.False(result.ShouldExit);
        Assert.Contains("crew failed", result.Message);
    }

    [Fact]
    public async Task Execute_uses_RawInput_as_question_text()
    {
        var (cmd2, ctxBuilt, _, _, orch2, _) = Build();
        var ctx3 = ctxBuilt with { RawInput = "  What is 2+2?  " };

        await cmd2.ExecuteAsync(ctx3, CancellationToken.None);

        Assert.Single(orch2.Calls);
        Assert.Equal("What is 2+2?", orch2.Calls[0].Input.InitialContext);
    }

    [Fact]
    public async Task Execute_passes_question_as_variable_to_crew()
    {
        var (cmd, ctxBuilt, _, _, orch, _) = Build();
        var ctx = ctxBuilt with { RawInput = "What is the meaning?" };

        await cmd.ExecuteAsync(ctx, CancellationToken.None);

        Assert.Single(orch.Calls);
        var stringVars = orch.Calls[0].Input.GetStringVariables();
        Assert.Equal("What is the meaning?", stringVars["question"]);
    }
}
