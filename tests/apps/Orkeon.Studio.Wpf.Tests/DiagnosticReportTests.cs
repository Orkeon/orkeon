using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The diagnostic result must leave the window: WPF text blocks are not selectable, so the
/// screen exposes the whole report as plain text for the clipboard.
/// </summary>
public sealed class DiagnosticReportTests
{
    private static DiagnosticViewModel Create(FakeProcessLauncher launcher) =>
        new(new OrkeonProcessRunner(
            launcher,
            new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled(), ["orkeon"])));

    [Fact]
    public async Task The_copyable_report_carries_the_verdict_and_every_check_verbatim()
    {
        var launcher = new FakeProcessLauncher();
        launcher.OutputToEmit.Add(ProcessOutputLine.Now(
            ProcessOutputChannel.StandardOutput,
            """[{"check":"appsettings","status":"ok","detail":"/home/me/appsettings.json"},""" +
            """{"check":"llm-reachability","status":"fail","detail":"endpoint refused the connection"}]"""));

        var diagnostic = Create(launcher);
        Assert.False(diagnostic.CanCopyReport); // nothing to copy before a run

        await diagnostic.RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(diagnostic.CanCopyReport);
        var report = diagnostic.BuildReport();
        Assert.Contains("appsettings — /home/me/appsettings.json", report, StringComparison.Ordinal);
        Assert.Contains("llm-reachability — endpoint refused the connection", report, StringComparison.Ordinal);
        Assert.Contains(diagnostic.Summary!, report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_broken_run_still_yields_a_copyable_report_with_the_error()
    {
        var launcher = new FakeProcessLauncher();
        launcher.OutputToEmit.Add(ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, "not json at all"));

        var diagnostic = Create(launcher);
        await diagnostic.RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(diagnostic.CanCopyReport);
        Assert.Contains(diagnostic.ErrorMessage!, diagnostic.BuildReport(), StringComparison.Ordinal);
    }
}

/// <summary>The verdict card and the silent first run (audit 09/20).</summary>
public sealed class DiagnosticVerdictTests
{
    private static DiagnosticViewModel Create(FakeProcessLauncher launcher) =>
        new(new OrkeonProcessRunner(
            launcher,
            new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled(), ["orkeon"])));

    [Fact]
    public async Task The_counts_and_the_headline_follow_the_report()
    {
        var launcher = new FakeProcessLauncher();
        launcher.OutputToEmit.Add(ProcessOutputLine.Now(
            ProcessOutputChannel.StandardOutput,
            """[{"check":"appsettings","status":"ok","detail":"readable"},""" +
            """{"check":"llm-config","status":"ok","detail":"anthropic"},""" +
            """{"check":"llm-reachability","status":"warn","detail":"slow"}]"""));

        var diagnostic = Create(launcher);
        // The window's InitializeAsync path is the silent first run.
        await diagnostic.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, diagnostic.OkCount);
        Assert.Equal(1, diagnostic.WarningCount);
        Assert.Equal(0, diagnostic.FailureCount);
        Assert.True(diagnostic.HasIssues);
        Assert.Equal("One point to fix before launching a team.", diagnostic.VerdictHeadline);
        Assert.Contains("2", diagnostic.VerdictDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_green_report_says_everything_is_in_place_and_checks_speak_plainly()
    {
        var launcher = new FakeProcessLauncher();
        launcher.OutputToEmit.Add(ProcessOutputLine.Now(
            ProcessOutputChannel.StandardOutput,
            """[{"check":"appsettings","status":"ok","detail":"readable"}]"""));

        var diagnostic = Create(launcher);
        await diagnostic.RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(diagnostic.HasIssues);
        Assert.Equal("Everything is in place.", diagnostic.VerdictHeadline);
        // The plain-language overlay resolves per check id; the raw id stays for the expert.
        Assert.Equal("The settings file is readable", diagnostic.Checks[0].FriendlyName);
        Assert.Equal("appsettings", diagnostic.Checks[0].Name);
    }
}
