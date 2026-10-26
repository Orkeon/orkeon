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
        Assert.Equal("1 point to fix before launching a team.", diagnostic.VerdictHeadline);
        // D02: the detail counts the rows listed - the valid settings-file line is the third pass.
        Assert.Equal("3 checks passed, 1 warning, 0 failures.", diagnostic.VerdictDetail);
    }

    [Fact]
    public async Task The_detail_counts_the_rows_listed_the_settings_file_included()
    {
        // 062: four green checks, one warning, one failure, and a settings file with errors - the
        // list shows two failures, so the detail says two, in step with « 3 points to fix ».
        var launcher = new FakeProcessLauncher();
        launcher.OutputToEmit.Add(ProcessOutputLine.Now(
            ProcessOutputChannel.StandardOutput,
            """[{"check":"cli","status":"ok","detail":"orkeon"},""" +
            """{"check":"appsettings","status":"ok","detail":"readable"},""" +
            """{"check":"mounts","status":"warn","detail":"missing"},""" +
            """{"check":"llm-provider","status":"ok","detail":"deepseek"},""" +
            """{"check":"llm-reachability","status":"fail","detail":"refused"},""" +
            """{"check":"workspace","status":"ok","detail":"writable"}]"""));
        var diagnostic = Create(launcher);
        diagnostic.WatchSettingsFile(() => true);

        await diagnostic.RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(4, diagnostic.ShownOkCount);
        Assert.Equal(1, diagnostic.ShownWarningCount);
        Assert.Equal(2, diagnostic.ShownFailureCount);
        Assert.Equal(diagnostic.IssueCount, diagnostic.ShownWarningCount + diagnostic.ShownFailureCount);
        Assert.Equal("3 points to fix before launching a team.", diagnostic.VerdictHeadline);
        Assert.Equal("4 checks passed, 1 warning, 2 failures.", diagnostic.VerdictDetail);
    }

    [Fact]
    public async Task A_missing_engine_is_the_banner_not_a_failure_without_a_row()
    {
        // 063: doctor never started; the only row is the valid settings file. The point to fix
        // is the engine banner, so the detail counts one pass and no failure.
        var diagnostic = new DiagnosticViewModel(new OrkeonProcessRunner(
            new FakeProcessLauncher(), new OrkeonBinaryLocator(new FakeExecutableProbe())));
        diagnostic.WatchSettingsFile(() => false);

        await diagnostic.RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(diagnostic.IsEngineMissing);
        Assert.NotNull(diagnostic.ErrorMessage);
        Assert.Empty(diagnostic.Checks);
        Assert.Equal(1, diagnostic.ShownOkCount);
        Assert.Equal(0, diagnostic.ShownFailureCount);
        Assert.Equal("1 point to fix before launching a team.", diagnostic.VerdictHeadline);
        Assert.Equal("1 check passed, 0 warnings, 0 failures.", diagnostic.VerdictDetail);
    }

    [Fact]
    public async Task An_unreadable_report_is_not_a_missing_engine()
    {
        var launcher = new FakeProcessLauncher();
        launcher.OutputToEmit.Add(ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, "not json at all"));
        var diagnostic = Create(launcher);

        await diagnostic.RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(diagnostic.ErrorMessage);
        Assert.False(diagnostic.IsEngineMissing);
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

    [Fact]
    public async Task The_profile_checks_doctor_added_speak_plainly_too()
    {
        // STUDIO-49: doctor covers Llm:Profiles in one line, and warns once per profile whose
        // ApiKeyEnvVar names a variable set nowhere.
        var launcher = new FakeProcessLauncher();
        launcher.OutputToEmit.Add(ProcessOutputLine.Now(
            ProcessOutputChannel.StandardOutput,
            """[{"check":"llm-profiles","status":"ok","detail":"1 profile(s)"},{"check":"llm-profile-key","status":"warn","detail":"Llm:Profiles:z-ai:ApiKeyEnvVar names an environment variable that is not set"}]"""));

        var diagnostic = Create(launcher);
        await diagnostic.RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("The LLM profiles", diagnostic.Checks[0].FriendlyName);
        Assert.Equal("The key of an LLM profile", diagnostic.Checks[1].FriendlyName);
    }
}
