using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The unified "Réglages" screen (design v3): the model profiles, their two elections, the
/// mirror of the default into the Llm section, and the tab gating by mode.
/// </summary>
public sealed class SettingsScreenTests
{
    private static (ModelProfilesViewModel Profiles, LlmSectionViewModel Llm, InMemoryModelProfileStore Store, AppSettingsDocument Document) Build()
    {
        var document = AppSettingsDocument.CreateEmpty();
        var llm = new LlmSectionViewModel(() => document, () => { }, new FakeLlmEndpointProbe());
        var store = new InMemoryModelProfileStore();
        var profiles = new ModelProfilesViewModel(store, llm, probe: new FakeLlmEndpointProbe());
        return (profiles, llm, store, document);
    }

    private static ModelProfile Ollama(string name) =>
        new() { Name = name, Provider = "Ollama", Model = "qwen2.5:14b", BaseUrl = "http://localhost:11434/v1" };

    // ── profiles ──

    [Fact]
    public async Task Creating_the_first_profile_elects_it_and_mirrors_it_into_the_llm_section()
    {
        var (profiles, llm, store, _) = Build();

        profiles.NewProfileCommand.Execute(null);
        profiles.Editor!.Name = "Local rapide";
        profiles.Editor.SaveCommand.Execute(null);

        Assert.Null(profiles.Editor);
        Assert.Equal("Local rapide", profiles.DefaultProfileName);
        // The first preset seeded the endpoint; electing the default writes it to the document.
        Assert.NotNull(llm.Model);
        Assert.NotNull(llm.BaseUrl);
        var persisted = await store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Single(persisted.Profiles);
    }

    [Fact]
    public void Electing_a_default_rewrites_the_llm_section_to_that_profile()
    {
        var (profiles, llm, _, _) = Build();
        profiles.CommitEdit(Ollama("Local"), previousName: null);
        profiles.CommitEdit(
            new ModelProfile { Name = "Cloud", Provider = "OpenAI", Model = "gpt-4.1-mini", BaseUrl = "https://api.openai.com/v1" },
            previousName: null);

        profiles.SetDefault("Cloud");

        Assert.Equal("Cloud", profiles.DefaultProfileName);
        Assert.Equal("gpt-4.1-mini", llm.Model);
        Assert.Equal("https://api.openai.com/v1", llm.BaseUrl);
    }

    [Fact]
    public void The_assistants_profile_is_a_separate_election_that_gates_nothing_else()
    {
        var (profiles, _, _, _) = Build();
        profiles.CommitEdit(Ollama("Local"), previousName: null);

        Assert.False(profiles.HasStudioProfile);

        profiles.StudioProfileName = "Local";

        Assert.True(profiles.HasStudioProfile);
        Assert.Equal("Local", profiles.Set.Studio?.Name);
    }

    [Fact]
    public void Deleting_is_refused_on_the_last_profile()
    {
        var (profiles, _, _, _) = Build();
        profiles.CommitEdit(Ollama("Only"), previousName: null);

        Assert.False(profiles.CanDelete);
        Assert.False(profiles.Profiles[0].DeleteCommand.CanExecute(null));
    }

    [Fact]
    public void Duplication_appends_a_uniquely_named_copy()
    {
        var (profiles, _, _, _) = Build();
        profiles.CommitEdit(Ollama("Local"), previousName: null);

        profiles.Profiles[0].DuplicateCommand.Execute(null);

        Assert.Equal(2, profiles.Profiles.Count);
        Assert.Equal("Local (copy)", profiles.Profiles[1].Name);
    }

    [Fact]
    public async Task The_editor_probes_the_endpoint_and_reports_the_answer()
    {
        var (profiles, _, _, _) = Build();
        profiles.BeginEdit(Ollama("Local"));

        await profiles.Editor!.TestConnectionAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(profiles.Editor.ConnectionTestResult);
    }

    // ── the tab gating ──

    private static SettingsScreenViewModel Screen(UiModeViewModel mode)
    {
        var (profiles, _, _, _) = Build();
        var config = new ConfigTabViewModel(new FakeAppSettingsStore(), new FakeDirectoryProbe());
        return new SettingsScreenViewModel(config, profiles, mode);
    }

    [Fact]
    public void An_expert_tab_requested_in_novice_mode_falls_back_to_the_model_tab()
    {
        var screen = Screen(new UiModeViewModel());

        screen.ShowJsonCommand.Execute(null);

        Assert.True(screen.IsModelTab);
    }

    [Fact]
    public void Switching_back_to_novice_leaves_no_blank_screen_behind()
    {
        var mode = new UiModeViewModel("expert");
        var screen = Screen(mode);
        screen.ShowLimitsCommand.Execute(null);
        Assert.True(screen.IsLimitsTab);

        mode.SetNoviceCommand.Execute(null);

        Assert.True(screen.IsModelTab);
    }

    [Fact]
    public void The_folders_tab_is_open_to_both_modes()
    {
        var screen = Screen(new UiModeViewModel());

        screen.ShowFoldersCommand.Execute(null);

        Assert.True(screen.IsFoldersTab);
    }
}
