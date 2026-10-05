using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Tests.Doubles;

namespace Orkeon.Studio.Core.Tests.Forge;

/// <summary>
/// <c>forge rephrase</c> on Studio's side of the wire (STUDIO-57): the argv carries the request
/// whole, the one <c>need.rephrased</c> line is the result, and an <c>error</c> line, stderr or
/// the exit code is the failure.
/// </summary>
public sealed class ForgeRephraseProtocolTests
{
    private const string InstallDirectory = "/opt/orkeon";
    private static readonly string BinaryPath = Path.Combine(InstallDirectory, "orkeon");

    private static ForgeClient Client(FakeProcessLauncher processes) =>
        new(processes, new OrkeonBinaryLocator(new FakeExecutableProbe { BaseDirectory = InstallDirectory }.WithFile(BinaryPath)));

    [Fact]
    public async Task The_request_travels_whole_and_the_rewritten_text_comes_back()
    {
        var processes = new FakeProcessLauncher().WithStandardOutput(
            """{"v":2,"seq":1,"ts":"t","kind":"need.rephrased","text":"L'équipe lit deux dossiers et envoie les mails.","original":"deux dossiers, envoyer"}""");

        var result = await Client(processes).RephraseAsync(
            new ForgeRephraseRequest { Need = "deux dossiers,\nenvoyer", WorkingDirectory = "/ws", SettingsPath = "/s.json" },
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("L'équipe lit deux dossiers et envoie les mails.", result.Text);
        Assert.Null(result.Error);
        var request = Assert.Single(processes.Requests);
        Assert.Equal(BinaryPath, request.FileName);
        Assert.Equal(["forge", "rephrase", "deux dossiers,\nenvoyer", "--events", "jsonl", "--settings", "/s.json"], request.Arguments);
        Assert.Equal("/ws", request.WorkingDirectory);
    }

    [Fact]
    public async Task An_error_line_then_stderr_then_the_exit_code_say_why_there_is_no_text()
    {
        var refused = new FakeProcessLauncher { ExitCode = 1 }.WithStandardOutput(
            """{"v":2,"seq":1,"ts":"t","kind":"error","code":"FORGE-LLM-UNAVAILABLE","message":"no LLM is configured","recoverable":false}""");
        var result = await Client(refused).RephraseAsync(new ForgeRephraseRequest { Need = "x" }, TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.Equal("no LLM is configured", result.Error);

        var crashed = new FakeProcessLauncher { ExitCode = 2 }.WithStandardError("Unhandled: boom");
        result = await Client(crashed).RephraseAsync(new ForgeRephraseRequest { Need = "x" }, TestContext.Current.CancellationToken);
        Assert.Equal("Unhandled: boom", result.Error);

        var silent = new FakeProcessLauncher { ExitCode = 3 };
        result = await Client(silent).RephraseAsync(new ForgeRephraseRequest { Need = "x" }, TestContext.Current.CancellationToken);
        Assert.Equal("exit code 3", result.Error);
    }

    [Fact]
    public async Task Without_the_binary_nothing_is_launched()
    {
        var processes = new FakeProcessLauncher();
        var client = new ForgeClient(processes, new OrkeonBinaryLocator(new FakeExecutableProbe { BaseDirectory = InstallDirectory }));

        var result = await client.RephraseAsync(new ForgeRephraseRequest { Need = "x" }, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Empty(processes.Requests);
    }
}
