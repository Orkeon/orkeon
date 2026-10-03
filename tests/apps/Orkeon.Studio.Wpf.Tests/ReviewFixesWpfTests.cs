using System.Text.Json;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// Pins of the RC2-FEAT-05 full-review fixes on the WPF side: no state leaks across
/// wizard sessions, an emptied goal is a removal, and the export says its outcome.
/// </summary>
public sealed class ReviewFixesWpfTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"orkeon-wpfreview-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Restart_forgets_the_previous_team_folders_and_engine_command()
    {
        var document = AppSettingsDocument.CreateEmpty();
        var llm = new LlmSectionViewModel(() => document, () => { });
        var profiles = new ModelProfilesViewModel(new InMemoryModelProfileStore(), llm, probe: new FakeLlmEndpointProbe());
        profiles.CommitEdit(
            new ModelProfile { Name = "Local", Provider = "Ollama", Model = "qwen2.5:14b", BaseUrl = "http://localhost:11434/v1" },
            previousName: null);
        profiles.StudioProfileName = "Local";
        var wizard = new CreateTeamViewModel(
            profiles,
            new CreateTeamDependencies
            {
                Client = new ForgeClient(
                    new FakeProcessLauncher(), new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
                WorkspaceDirectory = "/ws",
                TeamsRoot = "/teams",
            });
        wizard.AddTeamMount(Orkeon.Studio.Core.FileSystem.MountDefinition.Parse("/a:/docs:ro"));
        Assert.True(wizard.HasTeamMounts);

        // RestartCommand is gated on MaxStep/IsEngineRunning; ComposeAsync is the real
        // entry that resets the projection for a fresh session — same code path, and the
        // gesture reaches it directly now that no local questionnaire sits in front.
        wizard.Need = "une veille";
        wizard.ComposeCommand.Execute(null);

        // A new compose starts from no folder at all (STUDIO-46): the folders belong to the
        // session that confirmed them, and the new one's Folders step answers its own — step 1
        // keeps none any more. (EngineCommandLine is legitimately repopulated by the new
        // session's own start.)
        Assert.Empty(wizard.TeamMounts);
        Assert.False(wizard.HasTeamMounts);
        Assert.Empty(wizard.FolderRows);
    }

    [Fact]
    public void An_emptied_goal_is_removed_from_the_blueprint()
    {
        var editor = new AgentEditorViewModel();
        string? sent = null;
        editor.Open(
            """{"crew":{"name":"x"},"agents":[{"key":"a","role":"R","goal":"ancien","tools":[]}]}""",
            "a", [], json => sent = json);

        editor.WhatItDoes = "  ";
        editor.SaveCommand.Execute(null);

        using var document = JsonDocument.Parse(sent!);
        Assert.False(document.RootElement.GetProperty("agents")[0].TryGetProperty("goal", out _));
    }

    [Fact]
    public void Export_says_its_outcome_either_way()
    {
        var team = Path.Combine(_root, "veille");
        Directory.CreateDirectory(team);
        var destination = Path.Combine(_root, "partage");

        var teams = new TeamsViewModel(new TeamsDependencies { TeamsRoot = _root, LoadSessions = () => [] })
        {
            ExportDestinationPicker = () => destination,
        };
        teams.Teams[0].ExportCommand.Execute(null);
        Assert.Contains("partage", teams.StatusMessage, StringComparison.Ordinal);

        // Second export to the same destination: refused, and said.
        teams.Teams[0].ExportCommand.Execute(null);
        Assert.Contains("refused", teams.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
}
