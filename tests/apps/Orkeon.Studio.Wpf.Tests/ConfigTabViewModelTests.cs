using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Validation;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Services;
using Orkeon.Studio.Core.Localization;

namespace Orkeon.Studio.Wpf.Tests;

public sealed class ConfigTabViewModelTests
{
    private const string GlobalPath = "/home/user/.config/Orkeon/appsettings.json";

    private static ConfigTabViewModel Build(
        FakeAppSettingsStore? store = null,
        FakeDirectoryProbe? directories = null,
        FakePathPicker? picker = null,
        FakeProcessLauncher? launcher = null) =>
        new(new StudioServices
            {
                SettingsStore = store ?? new FakeAppSettingsStore(),
                Directories = directories ?? new FakeDirectoryProbe(),
                Picker = picker ?? new FakePathPicker(),
                ProcessRunner = new OrkeonProcessRunner(
                    launcher ?? new FakeProcessLauncher(),
                    new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            },
            globalPathOverride: GlobalPath);

    [Fact]
    public void Should_DefaultToTheGlobalLocation()
    {
        var tab = Build();

        Assert.True(tab.Location.IsGlobal);
        Assert.Equal(GlobalPath, tab.Location.EffectivePath);
    }

    [Fact]
    public void Should_ShowTheResolutionChain_So_TheUserKnowsWhichFileWins()
    {
        var tab = Build();

        Assert.Equal(4, tab.Location.ResolutionChain.Count);
    }

    [Fact]
    public void Should_WarnWithWin01_When_ThereIsNoLlmSection()
    {
        var tab = Build();

        Assert.True(tab.HasLlmWarning);
        Assert.Contains(tab.ValidationMessages, m => m.Code == ValidationCodes.LlmSectionMissing);
    }

    [Fact]
    public async Task Should_SaveDespiteWin01_Because_TheWarningIsNotBlocking()
    {
        var store = new FakeAppSettingsStore();
        var tab = Build(store, new FakeDirectoryProbe("/data"));
        DeclareAMount(tab);

        Assert.True(await tab.SaveAsync(TestContext.Current.CancellationToken));

        Assert.Equal([GlobalPath], store.SavedPaths);
        Assert.True(tab.HasLlmWarning);
        Assert.Contains("WIN-01", tab.StatusMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_RefuseToSave_When_NoMountIsDeclared()
    {
        // Tolerated while editing — a launcher can still pass --mount — but a settings file is
        // expected to stand on its own, so writing one that cannot start a run is blocked.
        var store = new FakeAppSettingsStore();
        var tab = Build(store);

        Assert.Contains(
            tab.Validate(),
            m => m.Code == ValidationCodes.MountsEmpty && m.Severity == ValidationSeverity.Warning);

        Assert.False(await tab.SaveAsync(TestContext.Current.CancellationToken));

        Assert.Empty(store.SavedPaths);
        Assert.Contains(
            tab.ValidationMessages,
            m => m.Code == ValidationCodes.MountsEmpty && m.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public async Task Should_RefuseToSave_When_ValidationFoundAnError()
    {
        var store = new FakeAppSettingsStore();
        var tab = Build(store);
        tab.Rag.Profile = "turbo";

        Assert.False(await tab.SaveAsync(TestContext.Current.CancellationToken));

        Assert.Empty(store.SavedPaths);
        Assert.True(tab.HasBlockingErrors);
    }

    [Fact]
    public async Task Should_PreserveUnknownKeys_When_TheFileIsLoadedEditedAndSaved()
    {
        // The lossless round-trip of §4.1: Studio must never drop configuration it does not model.
        const string path = "/crews/appsettings.json";
        var store = new FakeAppSettingsStore();
        store.Files[path] = """
        {
          "Llm": { "Model": "m", "BaseUrl": "https://api.openai.com/v1" },
          "SomeThirdPartySection": { "Nested": { "Flag": true } },
          "Orkeon": { "FileSystem": { "Mounts": ["/data:/workspace:ro"] } }
        }
        """;
        var tab = Build(store, new FakeDirectoryProbe("/data"));

        Assert.True(await tab.LoadAsync(path, TestContext.Current.CancellationToken));
        tab.Llm.Model = "other";
        Assert.True(await tab.SaveAsync(TestContext.Current.CancellationToken));

        Assert.Contains("SomeThirdPartySection", store.LastSavedJson!, StringComparison.Ordinal);
        Assert.Contains("\"Flag\": true", store.LastSavedJson!, StringComparison.Ordinal);
        Assert.Contains("\"other\"", store.LastSavedJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_PointTheSaveLocationAtTheFile_When_OneIsLoaded()
    {
        const string path = "/crews/appsettings.json";
        var store = new FakeAppSettingsStore();
        store.Files[path] = "{}";
        var tab = Build(store);

        await tab.LoadAsync(path, TestContext.Current.CancellationToken);

        Assert.True(tab.Location.IsCustom);
        Assert.Equal(path, tab.Location.EffectivePath);
    }

    [Fact]
    public async Task Should_ReportTheReason_When_TheFileCannotBeParsed()
    {
        const string path = "/crews/appsettings.json";
        var store = new FakeAppSettingsStore();
        store.Files[path] = "{ not json";
        var tab = Build(store);

        Assert.False(await tab.LoadAsync(path, TestContext.Current.CancellationToken));
        Assert.NotNull(tab.StatusMessage);
    }

    [Fact]
    public async Task Should_LoadTheMountsIntoTheEditor_When_AFileIsOpened()
    {
        const string path = "/crews/appsettings.json";
        var store = new FakeAppSettingsStore();
        store.Files[path] = """{"Orkeon":{"FileSystem":{"Mounts":["/data:/workspace:rw"]}}}""";
        var tab = Build(store, new FakeDirectoryProbe("/data"));

        await tab.LoadAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal("/data:/workspace:rw", Assert.Single(tab.Mounts.Mounts).MountString);
    }

    [Fact]
    public async Task Should_WriteTheMountArray_When_AMountIsAddedInTheEditor()
    {
        var store = new FakeAppSettingsStore();
        var tab = Build(store, new FakeDirectoryProbe("/data"));

        var mount = tab.Mounts.AddMount();
        mount.PhysicalPath = "/data";
        mount.VirtualPath = "/docs";
        await tab.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Contains("/data:/docs:ro", store.LastSavedJson!, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_RefreshTheRawView_When_AFieldIsEdited()
    {
        var tab = Build();

        tab.Llm.Model = "gpt-4o-mini";

        Assert.Contains("gpt-4o-mini", tab.RawJson, StringComparison.Ordinal);
        Assert.True(tab.IsDirty);
    }

    [Fact]
    public async Task Should_ClearTheDirtyFlag_When_TheFileIsSaved()
    {
        var tab = Build(directories: new FakeDirectoryProbe("/data"));
        DeclareAMount(tab);
        tab.Llm.Model = "m";

        await tab.SaveAsync(TestContext.Current.CancellationToken);

        Assert.False(tab.IsDirty);
    }

    /// <summary>The one mount a saveable settings file needs; the probe must know its path.</summary>
    private static void DeclareAMount(ConfigTabViewModel tab) =>
        tab.Mounts.AddMount().PhysicalPath = "/data";

    [Fact]
    public async Task Should_ParseTheDoctorReport_When_TheDiagnosticRuns()
    {
        var launcher = new FakeProcessLauncher();
        launcher.OutputToEmit.Add(ProcessOutputLine.Now(
            ProcessOutputChannel.StandardOutput,
            """[{"check":"dotnet","status":"ok","detail":"10.0.301"},{"check":"llm","status":"warn","detail":"no key"}]"""));
        var tab = Build(launcher: launcher);

        await tab.Diagnostic.RunAsync(workingDirectory: null, TestContext.Current.CancellationToken);

        Assert.Equal(2, tab.Diagnostic.Checks.Count);
        Assert.Equal(DoctorStatus.Warning, tab.Diagnostic.Checks[1].Status);
        Assert.Null(tab.Diagnostic.ErrorMessage);
    }

    [Fact]
    public async Task Should_InvokeDoctorWithJson_So_TheCliRemainsTheAuthority()
    {
        var launcher = new FakeProcessLauncher();
        var tab = Build(launcher: launcher);

        await tab.Diagnostic.RunAsync(workingDirectory: null, TestContext.Current.CancellationToken);

        Assert.Equal(["doctor", "--json"], launcher.LastRequest!.Arguments);
    }

    [Fact]
    public async Task Should_ReportTheParseFailure_When_DoctorPrintsNoJson()
    {
        var launcher = new FakeProcessLauncher();
        launcher.OutputToEmit.Add(ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, "everything is fine"));
        var tab = Build(launcher: launcher);

        await tab.Diagnostic.RunAsync(workingDirectory: null, TestContext.Current.CancellationToken);

        Assert.NotNull(tab.Diagnostic.ErrorMessage);
        Assert.Empty(tab.Diagnostic.Checks);
    }

    [Fact]
    public void Should_ResetTheDocument_When_NewIsRequested()
    {
        var tab = Build();
        tab.Llm.Model = "m";

        tab.NewCommand.Execute(null);

        Assert.False(tab.Llm.Exists);
        Assert.False(tab.IsDirty);
    }
    [Fact]
    public void The_validation_findings_are_copyable_as_plain_text()
    {
        var tab = Build();
        tab.ValidateCommand.Execute(null);

        Assert.True(tab.CanCopyValidation == (tab.ValidationMessages.Count > 0));
        if (tab.CanCopyValidation)
        {
            var report = tab.BuildValidationReport();
            Assert.All(tab.ValidationMessages, m =>
                Assert.Contains(m.Display, report, StringComparison.Ordinal));
            if (tab.ValidationSummary is { Length: > 0 } summary)
                Assert.Contains(summary, report, StringComparison.Ordinal);
        }
    }

}

/// <summary>The one refusal a novice actually meets: no authorized folder yet.</summary>
public sealed class MountsEmptyRefusalTests
{
    [Fact]
    public async Task A_save_blocked_only_by_the_empty_folder_list_names_the_fix()
    {
        var tab = new ConfigTabViewModel(new StudioServices
        {
            SettingsStore = new FakeAppSettingsStore(),
            Directories = new FakeDirectoryProbe("/data"),
        });
        tab.Llm.Model = "phi3"; // dirty, but no mount declared

        Assert.False(await tab.SaveAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            EnglishStudioStrings.Instance[StudioStringKeys.ConfigNotSavedNeedFolder],
            tab.StatusMessage);

        // Authorizing a folder heals it: the very same save now goes through.
        tab.Mounts.AddMount().PhysicalPath = "/data";
        Assert.True(await tab.SaveAsync(TestContext.Current.CancellationToken));
    }
}
