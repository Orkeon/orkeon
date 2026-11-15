using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Code.Confinement;
using Orkeon.Tools.Code.DependencyInjection;
using Orkeon.Tools.Code.Sessions;
using Orkeon.Tools.Code.Tests.Doubles;

namespace Orkeon.Tools.Code.Tests;

/// <summary>
/// The interpreter opt-in "SECURITY:" line of <see cref="ShellCommandTool"/>: never logged when the tool is
/// built, logged once per host at the first call of an interpreter-enabled tool, with the effective confinement.
/// No process starts: every call is refused (unavailable backend, relative read-only path, forbidden operator).
/// </summary>
public sealed class ShellCommandToolSecurityLineTests
{
    private const string RefusedCommand = "node --version; id";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ToolCallRequest Request(string command) =>
        new(ToolName: "shell_command", Parameters: new Dictionary<string, object?>
        {
            ["command"] = command,
            ["working_directory"] = "/workspace",
        });

    private static ShellCommandTool Tool(
        RecordingLoggerFactory logs,
        ShellSecurityNotice notice,
        bool allowInterpreters = true,
        ShellConfinementOptions? confinement = null,
        ShellPlatform platform = ShellPlatform.Linux,
        IContainerDetector? detector = null,
        Func<BubblewrapAvailability>? bubblewrap = null) =>
        new(new FakeFileSystemService(), null, null, new Logger<ShellCommandTool>(logs), allowInterpreters, null, null, notice)
        {
            Confinement = confinement ?? new ShellConfinementOptions { Mode = ShellConfinementMode.Bubblewrap },
            Platform = platform,
            ContainerDetector = detector ?? new FakeContainerDetector(),
            Bubblewrap = bubblewrap ?? (() => BubblewrapAvailability.Unavailable("bwrap was not found")),
        };

    private static List<string> SecurityLines(RecordingLoggerFactory logs) =>
        [.. logs.Logger.Entries.Select(e => e.Message).Where(m => m.Contains("SECURITY", StringComparison.Ordinal))];

    [Fact]
    [Trait("BR", "CH-12")]
    public void ShouldLogNothing_WhenAnInterpreterEnabledToolIsConstructed()
    {
        using var logs = new RecordingLoggerFactory();

        using var tool = Tool(logs, new ShellSecurityNotice());

        Assert.Empty(logs.Logger.Entries);
    }

    [Fact]
    [Trait("BR", "CH-12")]
    public void ShouldLogNothing_WhenTheContainerEnumeratesTheToolsTwice()
    {
        using var logs = new RecordingLoggerFactory();
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddSingleton<ILoggerFactory>(logs);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Orkeon:Tools:Shell:AllowInterpreters"] = "true",
            ["Orkeon:Tools:Shell:Confinement:Mode"] = "None",
        }).Build());
        services.AddOrkeonCodeTools();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetServices<IBaseTool>().ToList();
        var second = provider.GetServices<IBaseTool>().ToList();

        Assert.Contains(first, t => t.Name == "shell_command");
        Assert.Contains(second, t => t.Name == "shell_command");
        Assert.Empty(SecurityLines(logs));
    }

    [Fact]
    [Trait("BR", "CH-13")]
    public async Task ShouldLogTheSecurityLineOnce_WhenTwoInstancesOfOneHostAreCalled()
    {
        using var logs = new RecordingLoggerFactory();
        var notice = new ShellSecurityNotice();
        using var one = Tool(logs, notice);
        using var two = Tool(logs, notice);

        await one.CallAsync(Request("echo a"), Ct);
        await two.CallAsync(Request("echo b"), Ct);

        var messages = logs.Logger.Entries.Select(e => e.Message).ToList();
        var line = Assert.Single(SecurityLines(logs));
        var refusal = messages.FindIndex(m => m.Contains("confinement is unavailable", StringComparison.Ordinal));
        Assert.True(refusal >= 0);
        Assert.True(messages.IndexOf(line) < refusal, "the SECURITY line comes before the command is handled");
    }

    [Fact]
    [Trait("BR", "CH-13")]
    public async Task ShouldNameTheBackend_WhenConfinementIsInForce()
    {
        using var logs = new RecordingLoggerFactory();
        var confinement = new ShellConfinementOptions { Mode = ShellConfinementMode.Bubblewrap, ReadOnlyPaths = { "opt/node" } };
        using var tool = Tool(logs, new ShellSecurityNotice(), confinement: confinement,
            bubblewrap: () => BubblewrapAvailability.Available("/usr/bin/bwrap"));

        await tool.CallAsync(Request("echo a"), Ct);

        var line = Assert.Single(SecurityLines(logs));
        Assert.Contains("allows interpreters", line, StringComparison.Ordinal);
        Assert.Contains("bubblewrap", line, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-13")]
    public async Task ShouldSayTheCallWillFail_WhenTheBackendIsUnavailable()
    {
        using var logs = new RecordingLoggerFactory();
        using var tool = Tool(logs, new ShellSecurityNotice());

        await tool.CallAsync(Request("echo a"), Ct);

        var line = Assert.Single(SecurityLines(logs));
        Assert.Contains("will fail", line, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("BR", "CH-13")]
    [InlineData(false, "Mode=None")]
    [InlineData(true, "running in a container")]
    public async Task ShouldSayUnconfinedAndWhy_WhenModeIsNoneOrInAContainer(bool inContainer, string reason)
    {
        using var logs = new RecordingLoggerFactory();
        var confinement = new ShellConfinementOptions { Mode = inContainer ? ShellConfinementMode.Auto : ShellConfinementMode.None };
        using var tool = Tool(logs, new ShellSecurityNotice(), confinement: confinement,
            detector: new FakeContainerDetector { InContainer = inContainer });

        await tool.CallAsync(Request(RefusedCommand), Ct);

        var line = Assert.Single(SecurityLines(logs));
        Assert.Contains("confinement: none", line, StringComparison.Ordinal);
        Assert.Contains(reason, line, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-13")]
    public async Task ShouldLogNoSecurityLine_WhenInterpretersAreOff()
    {
        using var logs = new RecordingLoggerFactory();
        using var tool = Tool(logs, new ShellSecurityNotice(), allowInterpreters: false);

        await tool.CallAsync(Request("echo a"), Ct);

        Assert.Empty(SecurityLines(logs));
    }
}
