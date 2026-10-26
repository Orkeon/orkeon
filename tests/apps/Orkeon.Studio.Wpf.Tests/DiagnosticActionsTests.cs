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
}
