using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The Diagnostic of the refonte (lot 5): one sentence and one action per problem, and an exact
/// count of the points to fix, the settings file included (the former sidebar card).
/// </summary>
public sealed class DiagnosticActionsTests
{
    private static DoctorCheckViewModel Check(string name, DoctorStatus status) =>
        new(new DoctorCheck(name, status, "detail", status.ToString()));

    [Theory]
    [InlineData("llm-config", DoctorCheckViewModel.FixModels)]
    [InlineData("llm-profiles", DoctorCheckViewModel.FixModels)]
    [InlineData("llm-profile-key", DoctorCheckViewModel.FixKeys)]
    [InlineData("llm-reachability", DoctorCheckViewModel.FixTest)]
    [InlineData("workspace-write", DoctorCheckViewModel.FixFolders)]
    [InlineData("appsettings", DoctorCheckViewModel.FixFile)]
    [InlineData("runner-settings", DoctorCheckViewModel.FixFile)]
    [InlineData("install-channel", DoctorCheckViewModel.FixAgain)]
    [InlineData("esbuild", DoctorCheckViewModel.FixAgain)]
    [InlineData("a-future-check", DoctorCheckViewModel.FixAgain)]
    public void Each_problem_carries_the_action_that_fixes_it(string name, string kind)
    {
        var check = Check(name, DoctorStatus.Failure);

        Assert.Equal(kind, check.FixKind);
        Assert.True(check.HasFix);
        Assert.False(string.IsNullOrWhiteSpace(check.FixLabel));
        Assert.False(Check(name, DoctorStatus.Ok).HasFix);
    }

    [Fact]
    public void A_problem_says_what_is_wrong_and_an_unknown_one_falls_back_to_its_status()
    {
        var strings = EnglishStudioStrings.Instance;

        Assert.Equal(strings["Studio.Diagnostics.Problem.llm-config"], Check("llm-config", DoctorStatus.Failure).Sentence);
        Assert.Equal(Check("llm-config", DoctorStatus.Ok).StatusSentence, Check("llm-config", DoctorStatus.Ok).Sentence);
        var unknown = Check("a-future-check", DoctorStatus.Warning);
        Assert.Equal(unknown.StatusSentence, unknown.Sentence);
    }

    [Fact]
    public async Task The_count_adds_the_settings_file_when_it_has_errors()
    {
        var launcher = new FakeProcessLauncher();
        launcher.OutputToEmit.Add(ProcessOutputLine.Now(
            ProcessOutputChannel.StandardOutput,
            """[{"check":"appsettings","status":"ok","detail":"readable"},""" +
            """{"check":"llm-config","status":"fail","detail":"none"},""" +
            """{"check":"llm-reachability","status":"warn","detail":"slow"}]"""));
        var diagnostic = new DiagnosticViewModel(new OrkeonProcessRunner(
            launcher,
            new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled(), ["orkeon"])));
        var fileInvalid = false;
        diagnostic.WatchSettingsFile(() => fileInvalid);

        Assert.Equal(0, diagnostic.IssueCount);
        await diagnostic.RunAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, diagnostic.IssueCount);
        Assert.Equal("2 points to fix before launching a team.", diagnostic.VerdictHeadline);

        fileInvalid = true;
        diagnostic.RefreshSettingsFile();

        Assert.True(diagnostic.IsSettingsFileInvalid);
        Assert.Equal(3, diagnostic.IssueCount);
        Assert.Equal(EnglishStudioStrings.Instance[StudioStringKeys.DiagSettingsFileInvalid], diagnostic.SettingsFileSentence);
    }

    private static DiagnosticViewModel Installed(FakeProcessLauncher launcher) =>
        new(new OrkeonProcessRunner(
            launcher,
            new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled(), ["orkeon"])));

    [Fact]
    public async Task Failures_come_first_and_the_first_fix_is_the_first_failure()
    {
        // UX lot 04 (064): the list reads failures, then warnings, then the rest.
        var launcher = new FakeProcessLauncher();
        launcher.OutputToEmit.Add(ProcessOutputLine.Now(
            ProcessOutputChannel.StandardOutput,
            """[{"check":"appsettings","status":"ok","detail":"readable"},""" +
            """{"check":"llm-reachability","status":"warn","detail":"slow"},""" +
            """{"check":"llm-config","status":"fail","detail":"none"}]"""));
        var diagnostic = Installed(launcher);

        await diagnostic.RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["llm-config", "llm-reachability", "appsettings"], diagnostic.Checks.Select(c => c.Name));
        Assert.Equal("llm-config", diagnostic.FirstFixable?.Name);
        Assert.True(diagnostic.ShowVerdict);
        Assert.Null(diagnostic.DangerMessage);
    }

    [Fact]
    public async Task A_missing_engine_shows_one_banner_that_says_what_to_do()
    {
        // UX lot 04 (043-063, 065): the danger banner alone, and it says to install the engine.
        var diagnostic = new DiagnosticViewModel(new OrkeonProcessRunner(
            new FakeProcessLauncher(), new OrkeonBinaryLocator(new FakeExecutableProbe())));

        await diagnostic.RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(diagnostic.IsEngineMissing);
        Assert.False(diagnostic.ShowVerdict);
        Assert.Equal(EnglishStudioStrings.Instance[StudioStringKeys.DiagEngineMissingHow], diagnostic.DangerMessage);
        Assert.False(diagnostic.HasInstallGuide);
    }

    [Fact]
    public void The_install_guide_opens_only_an_https_page_in_the_browser()
    {
        var browser = new RecordingBrowserOpener();
        var page = new Uri("https://example.org/install");
        var runner = new OrkeonProcessRunner(new FakeProcessLauncher(), new OrkeonBinaryLocator(new FakeExecutableProbe()));

        var diagnostic = new DiagnosticViewModel(runner, browser: browser, installGuide: page);
        Assert.True(diagnostic.HasInstallGuide);
        diagnostic.OpenInstallGuideCommand.Execute(null);
        Assert.Equal([page], browser.Opened);

        var plain = new DiagnosticViewModel(runner, browser: browser, installGuide: new Uri("http://example.org/install"));
        Assert.False(plain.HasInstallGuide);
        Assert.False(plain.OpenInstallGuideCommand.CanExecute(null));
        Assert.False(new DiagnosticViewModel(runner, installGuide: page).HasInstallGuide);
    }
}
