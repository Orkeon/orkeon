using Orkeon.Tests.Shared.Timing;
using Jint;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Commands.Scripting.DependencyInjection;
using Orkeon.Cli.Commands.Scripting.Loading;
using Orkeon.Cli.Commands.Scripting.Runtime;
using Orkeon.Cli.Commands.Scripting.Tests.Fixtures;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Scripting;
using Orkeon.Scripting.Toolchain;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Cli.Commands.Scripting.Tests.Runtime;

/// <summary>
/// The session's tool-call rules in the REPL: a command sets them through <c>ctx.services.get("permissions")</c>, and
/// the permission gate reads them before the mode — an allowed call runs without a question, a denied one is refused,
/// and <c>dontAsk</c> refuses what no rule allows without asking.
/// </summary>
public sealed class PermissionRulesFacadeTests : IDisposable
{
    private readonly ScriptedTestConsole _console = new();
    private readonly ServiceProvider _services;

    public PermissionRulesFacadeTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Orkeon:Security:PermissionGate:Enabled"] = "true" })
            .Build();
        _services = new ServiceCollection()
            .AddSingleton<IConsoleAdapter>(_console)
            .AddConsoleToolApproval()
            .AddOrkeonPermissionGate(configuration)
            .BuildServiceProvider();
    }

    public void Dispose() => _services.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private PermissionRulesFacade Permissions()
    {
        var locator = new ScriptServiceLocator(DefaultScriptServiceWhitelist.Build().Build(), _services);
        Assert.True(locator.has(ScriptServiceKeys.Permissions));
        return Assert.IsType<PermissionRulesFacade>(locator.get(ScriptServiceKeys.Permissions));
    }

    private IPermissionGate Gate => _services.GetRequiredService<IPermissionGate>();

    [Fact]
    public async Task ShouldRunTheCallWithoutAQuestion_WhenACommandAllowedIt()
    {
        Permissions().allow("file_write");

        var verdict = await Gate.CheckAsync("file_write", new Dictionary<string, object?>(), "default", cancellationToken: Ct);

        Assert.Equal(PermissionAction.Allow, verdict.Action);
        Assert.DoesNotContain("approve", _console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldPutTheCallToTheOperator_WhenNoRuleCoversIt()
    {
        Permissions().allow("shell_command", "command", "npm test*");

        var pending = Gate.CheckAsync(
            "shell_command", new Dictionary<string, object?> { ["command"] = "npm run build" }, "default", cancellationToken: Ct);

        Assert.False(pending.IsCompleted);
        Assert.Contains("approve shell_command?", _console.Output, StringComparison.Ordinal);
        _services.GetRequiredService<ConsoleToolApprovalChannel>().TryConsume("n");
        Assert.Equal(PermissionAction.Deny, (await pending).Action);
    }

    [Fact]
    public async Task ShouldRefuseWithoutAQuestion_InDontAsk_WhenNoRuleAllowsTheCall()
    {
        var verdict = await Gate.CheckAsync("file_write", new Dictionary<string, object?>(), "dontAsk", cancellationToken: Ct);

        Assert.Equal(PermissionAction.Deny, verdict.Action);
        Assert.DoesNotContain("approve", _console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRefuseTheCall_WhenACommandDeniedIt()
    {
        Permissions().deny("shell_command", "command", "git push*");

        var verdict = await Gate.CheckAsync(
            "shell_command", new Dictionary<string, object?> { ["command"] = "git push" }, "bypassPermissions", cancellationToken: Ct);

        Assert.Equal(PermissionAction.Deny, verdict.Action);
        Assert.Contains("deny shell_command command=git push*", verdict.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldListTheRulesOncePerRuleAndClearThem()
    {
        var permissions = Permissions();
        permissions.allow("file_write");
        permissions.allow("file_write");
        permissions.deny("shell_command", "command", "rm *");

        Assert.Equal(["allow file_write", "deny shell_command command=rm *"], permissions.list());

        permissions.clear();
        Assert.Empty(permissions.list());
        Assert.Empty(_services.GetRequiredService<IToolPermissionRules>().Current);
    }

    [Fact]
    public void ShouldRefuseARule_WithoutATool_OrWithAPatternButNoArgument()
    {
        var permissions = Permissions();

        Assert.Throws<ArgumentException>(() => permissions.allow(" "));
        var error = Assert.Throws<ArgumentException>(() => permissions.allow("shell_command", null, "npm *"));
        Assert.Contains("name it", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldBeCalledFromAScript_WithItsOptionalArgumentsLeftOut()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        engine.SetValue("permissions", Permissions());

        var listed = engine.Evaluate(
            "permissions.allow('file_write'); permissions.allow('shell_command', 'command', 'npm test*'); permissions.list().join('|')");

        Assert.Equal("allow file_write|allow shell_command command=npm test*", listed.AsString());
    }

    [Fact]
    public void ShouldReportTheGateEnabled_WhenTheHostRegisteredOne()
    {
        Assert.True(Permissions().enabled);
    }

    [Fact]
    public void ShouldReportTheGateDisabled_WhenTheHostRegisteredNone()
    {
        using var ungated = Ungated();
        var locator = new ScriptServiceLocator(DefaultScriptServiceWhitelist.Build().Build(), ungated);

        var permissions = Assert.IsType<PermissionRulesFacade>(locator.get(ScriptServiceKeys.Permissions));

        Assert.False(permissions.enabled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShouldBeReadByAScriptRunAsACommand(bool gated)
    {
        using var ungated = gated ? null : Ungated();
        IServiceProvider services = ungated ?? _services;
        var directory = Path.Combine(Path.GetTempPath(), "orkeon-perm-cmd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "gate.cmd.ts"), """
                defineCommand({
                  name: "gate",
                  description: "Says whether the permission gate is on.",
                  async handler(args, ctx) {
                    ctx.writeLine("gate:" + ctx.services.get("permissions").enabled);
                    return ctx.continue();
                  },
                });
                """, Ct);
            var loader = new ScriptCommandLoader(
                new DiskBackedFileSystemService(directory, virtualRoot: "/cmd"),
                PassThroughTranspiler.Instance,
                new JsEngineFactory(),
                Microsoft.Extensions.Options.Options.Create(new ScriptCommandLoaderOptions { Directories = ["/cmd"], EsbuildTranspile = false }),
                new ScriptCommandLoaderDependencies
                {
                    Services = new ScriptServiceLocator(DefaultScriptServiceWhitelist.Build().Build(), services),
                });
            var (registry, _) = await loader.LoadAndRegisterAsync(Ct);
            var output = new ScriptedTestConsole();

            await registry.ScriptCommands.Single(c => c.Name == "gate").ExecuteAsync(
                new CommandContext { RawInput = "/gate", Args = [], Console = output, Scope = services }, Ct);

            Assert.Contains($"gate:{(gated ? "true" : "false")}", output.Output, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { /* best-effort */ }
        }
    }

    /// <summary>The REPL's rules without the gate: <c>Orkeon:Security:PermissionGate:Enabled</c> is not true.</summary>
    private ServiceProvider Ungated() => new ServiceCollection()
        .AddSingleton<IConsoleAdapter>(_console)
        .AddConsoleToolApproval()
        .AddOrkeonPermissionGate(new ConfigurationBuilder().Build())
        .BuildServiceProvider();

    [Fact]
    public void ShouldNotBeServed_WhenTheHostKeepsNoRules()
    {
        using var empty = new ServiceCollection().BuildServiceProvider();
        var locator = new ScriptServiceLocator(DefaultScriptServiceWhitelist.Build().Build(), empty);

        Assert.False(locator.has(ScriptServiceKeys.Permissions));
    }
}
