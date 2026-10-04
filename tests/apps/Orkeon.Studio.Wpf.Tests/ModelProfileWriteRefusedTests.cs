using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// STUDIO-56, decision 3: every change of the Models tab writes <c>studio-model-profiles.json</c>
/// after the previous write. A write that threw made every later one throw with it, without a word,
/// until the end of the session: the settings created or changed meanwhile were gone at the next
/// start. Each write now answers for itself, the tab says the failure on a line of its own, and the
/// next change writes the whole set again and clears the line.
/// </summary>
public sealed class ModelProfileWriteRefusedTests
{
    private static ModelProfile Profile(string name) =>
        new() { Name = name, Provider = "ollama", Model = "qwen3", BaseUrl = "http://localhost:11434/v1" };

    private static (ModelProfilesViewModel Tab, MockModelProfileStore Store, InMemoryModelProfileStore Inner) Tab()
    {
        var document = AppSettingsDocument.CreateEmpty();
        var inner = new InMemoryModelProfileStore();
        var store = new MockModelProfileStore(inner);
        var llm = new LlmSectionViewModel(() => document, () => { });
        var tab = new ModelProfilesViewModel(
            store, llm, EnglishStudioStrings.Instance, new FakeLlmEndpointProbe(), new FakeApiKeyStore());
        return (tab, store, inner);
    }

    public static TheoryData<Exception> Failures() => new()
    {
        new InvalidOperationException("the set cannot be written"),
        new IOException("the file is read-only"),
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task A_refused_write_is_said_and_the_next_change_is_written(Exception failure)
    {
        var (tab, store, inner) = Tab();
        await tab.InitializeAsync(TestContext.Current.CancellationToken);
        store.FailNext = failure;

        tab.CommitEdit(Profile("Local"), previousName: null);
        await tab.PendingWrite;

        Assert.True(tab.HasProfileWriteError);
        Assert.Contains(failure.Message, tab.ProfileWriteError, StringComparison.Ordinal);
        Assert.StartsWith("The model profiles file could not be written", tab.ProfileWriteError, StringComparison.Ordinal);

        tab.CommitEdit(Profile("Remote"), previousName: null);
        await tab.PendingWrite;

        Assert.Equal(2, store.Saves);
        Assert.False(tab.HasProfileWriteError);
        Assert.Null(tab.ProfileWriteError);
        var written = (await inner.LoadAsync(TestContext.Current.CancellationToken)).Set;
        Assert.Equal(["Local", "Remote"], written.Profiles.Select(profile => profile.Name));
    }

    [Fact]
    public async Task The_write_line_says_its_cause_again_in_the_new_language()
    {
        var strings = new FrenchOnDemandStrings();
        var document = AppSettingsDocument.CreateEmpty();
        var store = new MockModelProfileStore(new InMemoryModelProfileStore());
        var tab = new ModelProfilesViewModel(
            store, new LlmSectionViewModel(() => document, () => { }), strings, new FakeLlmEndpointProbe(), new FakeApiKeyStore());
        await tab.InitializeAsync(TestContext.Current.CancellationToken);
        store.FailNext = new IOException("disque plein");
        tab.CommitEdit(Profile("Local"), previousName: null);
        await tab.PendingWrite;
        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        strings.SwitchToFrench();

        Assert.Contains(nameof(ModelProfilesViewModel.ProfileWriteError), raised);
        Assert.StartsWith("Le fichier des profils de modèle n'a pas pu être écrit — disque plein.", tab.ProfileWriteError, StringComparison.Ordinal);
    }

    /// <summary>English, then the one French line this class reads.</summary>
    private sealed class FrenchOnDemandStrings : IStudioStrings
    {
        private bool _french;

        public string this[string key] =>
            _french && key == StudioStringKeys.ProfileFileNotWritten
                ? "Le fichier des profils de modèle n'a pas pu être écrit — {0}. Les réglages restent tels qu'affichés pour cette session ; la prochaine modification les écrit à nouveau."
                : EnglishStudioStrings.Instance[key];

        public event EventHandler? CultureChanged;

        public void SwitchToFrench()
        {
            _french = true;
            CultureChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
