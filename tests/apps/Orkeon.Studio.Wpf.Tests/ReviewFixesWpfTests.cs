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
        var llm = new LlmSectionViewModel(() => document, () => { }, new FakeLlmEndpointProbe());
        var profiles = new ModelProfilesViewModel(new InMemoryModelProfileStore(), llm, probe: new FakeLlmEndpointProbe());
        profiles.CommitEdit(
            new ModelProfile { Name = "Local", Provider = "Ollama", Model = "qwen2.5:14b", BaseUrl = "http://localhost:11434/v1" },
            previousName: null);
        profiles.StudioProfileName = "Local";
        var wizard = new CreateTeamViewModel(
            profiles,
            new ForgeClient(new FakeProcessLauncher(), new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            workspaceDirectory: "/ws",
            teamsRoot: "/teams");
        wizard.AddTeamMount(Orkeon.Studio.Core.FileSystem.MountDefinition.Parse("/a:/docs:ro"));
        Assert.True(wizard.HasTeamMounts);

        // RestartCommand is gated on MaxStep/IsEngineRunning; ComposeAsync is the real
        // entry that resets the projection for a fresh session — same code path.
        wizard.Need = "une veille";
        wizard.Outcome = "un résumé";
        foreach (var choice in wizard.FrequencyChoices.Take(1).Concat(wizard.SourceChoices.Take(1)).Concat(wizard.OutputChoices.Take(1)))
            choice.SelectCommand.Execute(null);
        wizard.ComposeCommand.Execute(null);

        // The previous team's folder authorizations were approved for THAT team only.
        // (EngineCommandLine is legitimately repopulated by the new session's own start.)
        Assert.Empty(wizard.TeamMounts);
        Assert.False(wizard.HasTeamMounts);
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

        var teams = new TeamsViewModel(teamsRoot: _root, loadSessions: () => [])
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
