using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Common;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// STUDIO-50 at adoption: the engine writes the team's launchers with the team's own folders and
/// nothing else, and the wizard writes them again from the sidecar it just saved — the run the
/// operating system schedules takes the team's model setting and every folder a Studio launch
/// gives it.
/// </summary>
public partial class CreateTeamWizardTests
{
    [Fact]
    public async Task Adopting_writes_the_launchers_again_with_the_teams_setting_and_its_folders()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orkeon-wizard-launchers-{Guid.NewGuid():N}");
        var promoted = Path.Combine(root, "veille-zai");
        var docs = new MountDefinition
        {
            Id = MountId.Create(),
            PhysicalPath = Path.Combine(root, "docs"),
            VirtualPath = "/docs",
            Rights = MountRights.ReadOnly,
        };
        try
        {
            var (vm, processes, profiles) = Build(teamsRoot: root, declaredMounts: () => [docs.ToMountString()]);
            profiles.CommitEdit(
                new ModelProfile
                {
                    Name = "Z.AI", Provider = "Z.AI", BaseUrl = "https://api.z.ai/api/paas/v4",
                    Model = "glm-5.2", KeyEnvName = "ZAI_API_KEY",
                },
                previousName: null);
            processes.OutputToEmit.AddRange(
            [
                Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/d","format":"yaml","resumed":false}"""),
                Out("""{"v":2,"seq":2,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""),
            ]);
            FillStepOne(vm);
            await Compose(vm);

            vm.TeamName = "Veille ZAI";
            vm.AdoptProfileName = "Z.AI";
            vm.AddTeamMount(docs);

            // The engine's promotion, as `forge promote` writes it: the crew and two launchers that
            // know nothing of the team's setting nor of a folder outside it.
            processes.WhileRunning = () =>
            {
                Directory.CreateDirectory(Path.Combine(promoted, "crew"));
                File.WriteAllText(Path.Combine(promoted, "crew", "config.yaml"), "name: veille\n");
                File.WriteAllText(Path.Combine(promoted, TeamLaunchers.PosixLauncherName), "#!/usr/bin/env sh\n# " + TeamLauncherScript.Header(Path.GetFileName(promoted)) + "\nexec orkeon run \"$DIR/crew\"\n");
                File.WriteAllText(Path.Combine(promoted, TeamLaunchers.WindowsLauncherName), "@echo off\r\nrem " + TeamLauncherScript.Header(Path.GetFileName(promoted)) + "\r\norkeon run \"%~dp0crew\"\r\n");
            };
            processes.OutputToEmit.Clear();
            processes.OutputToEmit.AddRange(
            [
                Out($$"""{"v":2,"seq":1,"ts":"t","kind":"promoted","path":{{System.Text.Json.JsonSerializer.Serialize(promoted)}},"launcher":"run.sh"}"""),
                Out("""{"v":2,"seq":2,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""),
            ]);
            await vm.SaveTeamCommand.ExecuteAsync();

            Assert.Equal("Z.AI", TeamCatalog.Describe(promoted).Profile);
            var posix = await File.ReadAllTextAsync(Path.Combine(promoted, TeamLaunchers.PosixLauncherName), TestContext.Current.CancellationToken);
            Assert.Contains("--llm-profile='z-ai'", posix, StringComparison.Ordinal);
            Assert.Contains($"--mount-id '{docs.Id}'", posix, StringComparison.Ordinal);
            Assert.Contains("Orkeon Studio writes this file again", posix, StringComparison.Ordinal);
            var windows = await File.ReadAllTextAsync(Path.Combine(promoted, TeamLaunchers.WindowsLauncherName), TestContext.Current.CancellationToken);
            Assert.Contains("--llm-profile=\"z-ai\"", windows, StringComparison.Ordinal);
            Assert.Contains($"--mount-id \"{docs.Id}\"", windows, StringComparison.Ordinal);
            Assert.DoesNotContain("ZAI_API_KEY", posix + windows, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// STUDIO-51: the adoption line says the engine's last warning in the sentence of its code — a
    /// schedule still installed in the engine's own words, never as a session folder the engine
    /// could not rename.
    /// </summary>
    [Fact]
    public async Task A_warning_the_wizard_has_no_sentence_for_is_said_in_the_engines_words()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orkeon-wizard-{Guid.NewGuid():N}");
        try
        {
            var (vm, processes) = await ReadyToAdoptAsync(root);
            vm.TeamName = "Ma veille";
            var promoted = Path.Combine(root, "ma-veille");
            processes.OutputToEmit.Clear();
            processes.OutputToEmit.AddRange(
            [
                Out($$"""{"v":2,"seq":1,"ts":"t","kind":"promoted","path":{{System.Text.Json.JsonSerializer.Serialize(promoted)}},"launcher":"run.sh"}"""),
                Out("""{"v":2,"seq":2,"ts":"t","kind":"warning","code":"FORGE-SCHEDULE-STILL-INSTALLED","message":"The team is no longer scheduled, but 'Orkeon ma-veille' still runs it."}"""),
                Out("""{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""),
            ]);

            await vm.SaveTeamCommand.ExecuteAsync();

            Assert.Null(vm.Failure);
            Assert.Equal(
                "Team “Ma veille” is saved in My teams. The team is no longer scheduled, but 'Orkeon ma-veille' still runs it.",
                vm.StatusMessage);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// STUDIO-51, decision 8: a team whose command is longer than a <c>cmd</c> command holds is
    /// adopted — its <c>run.cmd</c> launches nothing and says why, its <c>run.sh</c> is complete —
    /// and the adoption line says it once, in Studio's words: the engine's warning about the
    /// launchers it wrote is left out, Studio's own replaced them.
    /// </summary>
    [Fact]
    public async Task An_adopted_team_whose_run_cmd_launches_nothing_says_it_once()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orkeon-wizard-{Guid.NewGuid():N}");
        try
        {
            var (vm, processes) = await ReadyToAdoptAsync(root);
            vm.TeamName = "Veille longue";
            var promoted = Path.Combine(root, "veille-longue");
            var context = string.Concat(Enumerable.Repeat("abcdefghij", 900));
            processes.WhileRunning = () =>
            {
                Directory.CreateDirectory(Path.Combine(promoted, "crew"));
                File.WriteAllText(Path.Combine(promoted, "crew", "config.yaml"), "name: veille\n");
                File.WriteAllText(Path.Combine(promoted, TeamLaunchers.PosixLauncherName), "#!/usr/bin/env sh\n# " + TeamLauncherScript.Header(Path.GetFileName(promoted)) + "\nexec orkeon run \"$DIR/crew\"\n");
                File.WriteAllText(Path.Combine(promoted, TeamLaunchers.WindowsLauncherName), "@echo off\r\nrem " + TeamLauncherScript.Header(Path.GetFileName(promoted)) + "\r\norkeon run \"%~dp0crew\"\r\n");
                File.WriteAllText(
                    Path.Combine(promoted, ForgeSessionCatalog.TeamRecordFileName),
                    $$"""{"v":1,"slug":"veille","format":"yaml","brief":{"sample":{"initialContext":"{{context}}"} } }""");
            };
            processes.OutputToEmit.Clear();
            processes.OutputToEmit.AddRange(
            [
                Out($$"""{"v":2,"seq":1,"ts":"t","kind":"promoted","path":{{System.Text.Json.JsonSerializer.Serialize(promoted)}},"launcher":"run.sh"}"""),
                Out("""{"v":2,"seq":2,"ts":"t","kind":"warning","code":"FORGE-LAUNCHER-TOO-LONG","message":"The engine's own words about its launchers."}"""),
                Out("""{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""),
            ]);

            await vm.SaveTeamCommand.ExecuteAsync();

            Assert.Null(vm.Failure);
            Assert.StartsWith(
                "Team “Veille longue” is saved in My teams. Its run.cmd launches nothing on Windows: its command is ",
                vm.StatusMessage,
                StringComparison.Ordinal);
            Assert.EndsWith(" characters long, over the 8191 Windows accepts (longest option: --initial-context).", vm.StatusMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("engine's own words", vm.StatusMessage, StringComparison.Ordinal);
            Assert.Contains(context,
                await File.ReadAllTextAsync(Path.Combine(promoted, TeamLaunchers.PosixLauncherName), TestContext.Current.CancellationToken),
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
