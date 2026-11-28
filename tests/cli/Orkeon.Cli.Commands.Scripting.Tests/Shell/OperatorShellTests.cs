using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Commands.Scripting.DependencyInjection;
using Orkeon.Cli.Commands.Scripting.Shell;
using Orkeon.Cli.Commands.Scripting.Tests.Doubles;
using Orkeon.Infrastructure.Session;

namespace Orkeon.Cli.Commands.Scripting.Tests.Shell;

/// <summary>
/// A shell command the operator typed after <c>!</c> (#491): its output reaches the console as it comes, and the
/// session gets one note — tagged as the operator's own action, secrets masked, cut at a stated length.
/// </summary>
public sealed class OperatorShellTests
{
    private readonly FakeOperatorShellRunner _runner = new();
    private readonly InMemorySessionBufferService _session = new();
    private readonly FakeLineConsole _console = new();

    private OperatorShell Shell(bool enabled = true) =>
        new(_runner, new OperatorShellSettings(enabled, TimeSpan.FromSeconds(120)), _session);

    [Fact]
    public async Task TheOutput_ReachesTheConsole_AndTheSessionAsOneNoteTaggedAsTheOperatorsAction()
    {
        _runner.Lines.Add("b");

        await Shell().RunAsync("echo a | tr a b", _console, TestContext.Current.CancellationToken);

        Assert.Equal("b\n", _console.Output);
        var message = Assert.Single(_session.GetMessages());
        Assert.Equal("note", message.Role);
        Assert.NotEqual("user", message.Role);
        Assert.Equal(
            $"{OperatorShell.Header}\n$ echo a | tr a b\nb\n[exit code 0]\n{OperatorShell.Footer}",
            message.Content);
        Assert.NotNull(message.Timestamp);
    }

    [Fact]
    public async Task ANonZeroExitCode_IsPrinted_AndKeptInTheSession()
    {
        _runner.Lines.Add("fatal: not a git repository");
        _runner.Result = new OperatorShellRun(128, TimedOut: false);

        await Shell().RunAsync("git status", _console, TestContext.Current.CancellationToken);

        Assert.Contains("exit code 128", _console.Output);
        Assert.Contains("[exit code 128]", Assert.Single(_session.GetMessages()).Content);
    }

    [Fact]
    public async Task AZeroExitCode_IsNotPrinted()
    {
        _runner.Lines.Add("clean");

        await Shell().RunAsync("git status", _console, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("exit code", _console.Output);
    }

    [Fact]
    public async Task ATimeout_IsSaid_NamingTheSetting()
    {
        _runner.Result = new OperatorShellRun(null, TimedOut: true);

        await Shell().RunAsync("less README.md", _console, TestContext.Current.CancellationToken);

        Assert.Contains("stopped after 120 s (Orkeon:Cli:Repl:ShellPrefix:TimeoutSeconds)", _console.Output);
        Assert.Contains("[stopped after 120 s", Assert.Single(_session.GetMessages()).Content);
        Assert.Equal(TimeSpan.FromSeconds(120), _runner.Timeout);
    }

    [Fact]
    public async Task ASecretInTheOutput_IsMaskedInTheSession()
    {
        const string token = "ghp_abcdefghijklmnopqrstuvwxyz0123456789";
        _runner.Lines.Add($"GITHUB_TOKEN={token}");
        _runner.Lines.Add("api_key: s3cr3tValue1234567");

        await Shell().RunAsync("env | grep -i token", _console, TestContext.Current.CancellationToken);

        var content = Assert.Single(_session.GetMessages()).Content;
        Assert.DoesNotContain(token, content);
        Assert.DoesNotContain("s3cr3tValue1234567", content);
        Assert.Contains("***REDACTED***", content);
    }

    [Fact]
    public void ALongOutput_IsCutAtTheStatedLength_AndTheCutIsSaid()
    {
        var output = new string('x', OperatorShell.MaxSessionOutputCharacters + 500);

        var content = OperatorShell.SessionContent("cat big.log", output, "[exit code 0]");

        Assert.Contains(new string('x', OperatorShell.MaxSessionOutputCharacters), content);
        Assert.DoesNotContain(new string('x', OperatorShell.MaxSessionOutputCharacters + 1), content);
        Assert.Contains("[output cut at 10000 of 10500 characters]", content);
    }

    [Fact]
    public void Off_TheShellSaysWhyNamingTheSetting()
    {
        Assert.Null(Shell().DisabledReason);
        Assert.Contains("Orkeon:Cli:Repl:ShellPrefix:Enabled is false", Shell(enabled: false).DisabledReason);
    }

    [Fact]
    public void TheRegistration_IsOnWithATimeoutOf120Seconds_WhenTheSectionIsAbsent()
    {
        var shell = Registered(new Dictionary<string, string?>());

        Assert.Null(shell.GetRequiredService<IOperatorShell>().DisabledReason);
        Assert.Equal(TimeSpan.FromSeconds(120), shell.GetRequiredService<OperatorShellSettings>().Timeout);
    }

    [Fact]
    public void TheRegistration_ReadsTheSection()
    {
        var shell = Registered(new Dictionary<string, string?>
        {
            ["Orkeon:Cli:Repl:ShellPrefix:Enabled"] = "false",
            ["Orkeon:Cli:Repl:ShellPrefix:TimeoutSeconds"] = "30",
        });

        Assert.NotNull(shell.GetRequiredService<IOperatorShell>().DisabledReason);
        Assert.Equal(TimeSpan.FromSeconds(30), shell.GetRequiredService<OperatorShellSettings>().Timeout);
    }

    [Fact]
    public void TheRegistration_RefusesATimeoutThatIsNoNumberOfSecondsAboveZero()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Registered(new Dictionary<string, string?>
        {
            ["Orkeon:Cli:Repl:ShellPrefix:TimeoutSeconds"] = "0",
        }));

        Assert.Contains("Orkeon:Cli:Repl:ShellPrefix:TimeoutSeconds is '0'", ex.Message);
    }

    [Fact]
    public async Task TheRegisteredShell_WritesToTheSessionOfTheHost()
    {
        var services = new ServiceCollection()
            .AddSingleton<ISessionBufferService>(_session)
            .AddSingleton<IOperatorShellRunner>(_runner);
        services.AddReplShellPrefix(new ConfigurationBuilder().Build());
        _runner.Lines.Add("b");

        await services.BuildServiceProvider().GetRequiredService<IOperatorShell>()
            .RunAsync("echo b", _console, TestContext.Current.CancellationToken);

        Assert.Contains("$ echo b\nb\n", Assert.Single(_session.GetMessages()).Content);
    }

    private static ServiceProvider Registered(Dictionary<string, string?> values) =>
        new ServiceCollection()
            .AddReplShellPrefix(new ConfigurationBuilder().AddInMemoryCollection(values).Build())
            .BuildServiceProvider();
}
