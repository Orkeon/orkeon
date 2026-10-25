using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Services;
using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The Settings screen of the refonte (lot 5): the « N unsaved changes » bar, its Cancel, and
/// the question asked before leaving the screen with edits that are not saved.
/// </summary>
public sealed class SettingsPendingChangesTests
{
    private static (ConfigTabViewModel Config, SettingsScreenViewModel Screen, FakeAppSettingsStore Store) Build()
    {
        var store = new FakeAppSettingsStore();
        var config = new ConfigTabViewModel(
            new StudioServices { SettingsStore = store, Directories = new FakeDirectoryProbe("/data") });
        var profiles = new ModelProfilesViewModel(
            new InMemoryModelProfileStore(), config.Llm, probe: new FakeLlmEndpointProbe(), keyStore: new FakeApiKeyStore());
        var screen = new SettingsScreenViewModel(config, profiles, new UiModeViewModel("expert"));
        return (config, screen, store);
    }

    [Fact]
    public void The_diff_counts_each_changed_leaf_once()
    {
        Assert.Equal(0, SettingsJsonDiff.Count("""{"a":1,"b":{"c":"x"}}""", """{ "b": { "c": "x" }, "a": 1 }"""));
        Assert.Equal(1, SettingsJsonDiff.Count("""{"a":1,"b":{"c":"x"}}""", """{"a":2,"b":{"c":"x"}}"""));
        Assert.Equal(2, SettingsJsonDiff.Count("""{"a":1}""", """{"a":1,"n":{"p":1,"q":2}}"""));
        Assert.Equal(1, SettingsJsonDiff.Count("""{"a":1,"r":true}""", """{"a":1}"""));
    }

    [Fact]
    public void An_edit_shows_the_bar_with_its_count_and_cancel_brings_the_saved_values_back()
    {
        var (config, _, _) = Build();
        Assert.False(config.HasPendingChanges);

        var before = config.Rag.Profile;
        config.Rag.Profile = before == "quality" ? "fast" : "quality";

        Assert.True(config.HasPendingChanges);
        Assert.Equal(1, config.PendingChangeCount);
        Assert.Equal(
            EnglishStudioStrings.Instance.Format(StudioStringKeys.ConfigPendingChanges, 1),
            config.PendingChangesText);

        config.DiscardCommand.Execute(null);

        Assert.False(config.HasPendingChanges);
        Assert.Equal(before, config.Rag.Profile);
    }

    [Fact]
    public void Leaving_without_edits_goes_on_at_once()
    {
        var (_, screen, _) = Build();
        var left = 0;

        Assert.True(screen.TryLeave(() => left++));
        Assert.False(screen.IsAskingToLeave);
        Assert.Equal(0, left); // the caller goes on itself
    }

    [Fact]
    public void Leaving_with_edits_asks_and_stay_keeps_them()
    {
        var (config, screen, _) = Build();
        config.Rag.Profile = "quality";
        var left = 0;

        Assert.False(screen.TryLeave(() => left++));
        Assert.True(screen.IsAskingToLeave);

        screen.StayCommand.Execute(null);

        Assert.False(screen.IsAskingToLeave);
        Assert.True(config.HasPendingChanges);
        Assert.Equal(0, left);
    }

    [Fact]
    public void Leave_without_saving_drops_the_edits_then_goes_on()
    {
        var (config, screen, store) = Build();
        config.Rag.Profile = "quality";
        var left = 0;
        screen.TryLeave(() => left++);

        screen.DiscardAndLeaveCommand.Execute(null);

        Assert.Equal(1, left);
        Assert.False(screen.IsAskingToLeave);
        Assert.False(config.HasPendingChanges);
        Assert.Null(store.LastSavedJson);
    }

    [Fact]
    public async Task Save_and_leave_writes_the_file_then_goes_on()
    {
        var (config, screen, store) = Build();
        config.Rag.Profile = "quality";
        var left = 0;
        screen.TryLeave(() => left++);

        await screen.SaveAndLeaveCommand.ExecuteAsync(null);

        Assert.Equal(1, left);
        Assert.False(screen.IsAskingToLeave);
        Assert.Contains("\"quality\"", store.LastSavedJson!, StringComparison.Ordinal);
    }
}
