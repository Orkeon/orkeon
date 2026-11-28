using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Commands.Scripting.Configuration;
using Orkeon.Cli.TerminalGui.Hosting;

namespace Orkeon.ConsoleApp.Tests.DependencyInjection;

/// <summary>
/// The REPL's container offers the shell prefix (#491): on unless <c>Orkeon:Cli:Repl:ShellPrefix:Enabled</c> says
/// false. The container is the one <c>Program</c> composes; it is built, never run.
/// </summary>
public sealed class ReplShellPrefixWiringTests : IDisposable
{
    private readonly string _workspace = Directory.CreateTempSubdirectory("orkeon-repl-shell-").FullName;

    public void Dispose() => Directory.Delete(_workspace, recursive: true);

    private IHost Repl(params (string Key, string Value)[] settings) =>
        new HostBuilder()
            .ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(
                settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value))))
            .ConfigureServices((context, services) => Program.ConfigureServices(
                context, services, UiMode.Plain,
                ScriptedCommandsCliOptions.Parse(["--mount", $"{_workspace}:/workspace:ro"]),
                replWordWrap: false))
            .Build();

    [Fact]
    public void The_shell_prefix_is_on_by_default()
    {
        using var host = Repl();

        Assert.Null(host.Services.GetRequiredService<IOperatorShell>().DisabledReason);
    }

    [Fact]
    public void The_setting_turns_it_off()
    {
        using var host = Repl(("Orkeon:Cli:Repl:ShellPrefix:Enabled", "false"));

        Assert.Contains("Orkeon:Cli:Repl:ShellPrefix:Enabled", host.Services.GetRequiredService<IOperatorShell>().DisabledReason);
    }
}
