using Orkeon.Domain.Common;
using Orkeon.Studio.Core.FileSystem;
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
                File.WriteAllText(Path.Combine(promoted, TeamLaunchers.PosixLauncherName), "#!/usr/bin/env sh\nexec orkeon run \"$DIR/crew\"\n");
                File.WriteAllText(Path.Combine(promoted, TeamLaunchers.WindowsLauncherName), "@echo off\r\norkeon run \"%~dp0crew\"\r\n");
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
            Assert.Contains("--llm-profile 'z-ai'", posix, StringComparison.Ordinal);
            Assert.Contains($"--mount-id '{docs.Id}'", posix, StringComparison.Ordinal);
            Assert.Contains("Orkeon Studio writes this file again", posix, StringComparison.Ordinal);
            var windows = await File.ReadAllTextAsync(Path.Combine(promoted, TeamLaunchers.WindowsLauncherName), TestContext.Current.CancellationToken);
            Assert.Contains("--llm-profile \"z-ai\"", windows, StringComparison.Ordinal);
            Assert.Contains($"--mount-id \"{docs.Id}\"", windows, StringComparison.Ordinal);
            Assert.DoesNotContain("ZAI_API_KEY", posix + windows, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
