using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Hosting.Tests.Doubles;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// CH-23: a run host with interpreters enabled logs the shell SECURITY line at no build and once per host.
/// RunnerHost.InspectSettings clears its logging providers and has no logging hook: its half is proved
/// by code reading, not by a test.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerHostShellSecurityLineTests : IDisposable
{
    private const string Prefix = "SECURITY: shell_command";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-shell-line-" + Guid.NewGuid().ToString("N"));

    public RunnerHostShellSecurityLineTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private IHost Build(CapturingLoggerProvider capture)
    {
        var path = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(path, """{ "RaggableTree": { "Enabled": false }, "Orkeon": { "Tools": { "Shell": { "AllowInterpreters": true } } } }""");
        var original = Console.Error;
        using var muted = new StringWriter();
        Console.SetError(muted);
        try
        {
            return RunnerHost.Build(
                path,
                new RunnerMountPlan { CliMounts = [$"{_root}:/workspace:rw"], AllowExternalMounts = true },
                configureLogging: (_, logging) =>
                {
                    logging.SetMinimumLevel(LogLevel.Trace);
                    logging.AddProvider(capture);
                });
        }
        finally
        {
            Console.SetError(original);
        }
    }

    private static int SecurityLines(CapturingLoggerProvider capture) =>
        capture.Entries.Count(e => e.Message.StartsWith(Prefix, StringComparison.Ordinal));

    [Fact]
    [Trait("BR", "CH-23")]
    public void ShouldLogNoSecurityLine_WhenTheRunHostIsBuilt()
    {
        using var capture = new CapturingLoggerProvider();

        using var host = Build(capture);

        Assert.Equal(0, SecurityLines(capture));
    }

    [Fact]
    [Trait("BR", "CH-23")]
    public async Task ShouldLogTheSecurityLineOnce_WhenTheToolsAreResolvedAndValidatedTwice()
    {
        using var capture = new CapturingLoggerProvider();
        using var host = Build(capture);
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 2; i++)
        {
            using var scope = host.Services.CreateScope();
            var tool = scope.ServiceProvider.GetServices<IBaseTool>().First(t => t.Name == "shell_command");
            var response = await tool.CallAsync(
                new ToolCallRequest(ToolName: "shell_command", Parameters: new Dictionary<string, object?>
                {
                    ["command"] = "node --version; id",
                    ["working_directory"] = "/workspace",
                }),
                ct);

            // The command must stay refused at validation: nothing may run on the host here.
            Assert.False(response.Success);
        }

        Assert.Equal(1, SecurityLines(capture));
    }
}
