using Orkeon.Studio.Core.Forge;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>The Folders step across a resume (STUDIO-46).</summary>
public partial class CreateTeamWizardTests
{
    /// <summary>
    /// A session resumed at its dry pause brings its confirmed folders back as they were bound:
    /// the folder chosen on the disk behind /inpdf from <c>folders.json</c> — the brief never
    /// carries it — reaches the Composer row and the sidecar; /outmd, inside the team, stays a
    /// row the adoption answers inside the team.
    /// </summary>
    [Fact]
    public async Task A_resumed_session_brings_back_the_folders_it_confirmed_bound_as_they_were()
    {
        var root = Path.Combine(Path.GetTempPath(), "orkeon-wiz-folders-" + Guid.NewGuid().ToString("N"));
        var sessionDir = Path.Combine(root, ".orkeon", "forge", "veille");
        Directory.CreateDirectory(sessionDir);
        try
        {
            var ct = TestContext.Current.CancellationToken;
            await File.WriteAllTextAsync(Path.Combine(sessionDir, "session.json"),
                """{"v":1,"slug":"veille","format":"yaml","state":"Test","status":"Active"}""", ct);
            await File.WriteAllTextAsync(Path.Combine(sessionDir, "brief.json"),
                """{"goal":"Convertir","folders":[{"path":"/inpdf","role":"input"},{"path":"/outmd","role":"output"}],"acceptance":[]}""", ct);
            await File.WriteAllTextAsync(Path.Combine(sessionDir, "folders.json"),
                """{"folders":[{"path":"/inpdf","role":"input","dir":"/data/pdf"},{"path":"/outmd","role":"output"}]}""", ct);
            await File.WriteAllTextAsync(Path.Combine(sessionDir, "blueprint.json"),
                """{"crew":{"name":"veille"},"agents":[{"key":"a","role":"A","tools":["file_read","file_write"]}],"tasks":[{"key":"t","description":"d","agent":"a","deliverable":"/outmd/x.md"}]}""", ct);

            var (vm, processes, _) = Build(workspace: root);
            await vm.ResumeAsync(new ForgeSolutionSummary
            {
                Slug = "veille", State = "Test", Status = "Active", Directory = sessionDir,
            });

            Assert.Empty(processes.Requests);
            Assert.False(vm.IsFoldersStep);
            Assert.Equal(["/data/pdf:/inpdf:ro"], vm.TeamMounts);
            Assert.Equal("/data/pdf", vm.MountRows.Single(r => r.VirtualPath == "/inpdf").Folder);
            Assert.Equal(["/data/pdf:/inpdf:ro", "./outmd:/outmd:rw"], vm.SidecarMounts());
            Assert.DoesNotContain(vm.MountRows, r => r.VirtualPath is "/workspace" or "/output");

            // The engine keeps the folder in folders.json: the trial needs no --read for it.
            processes.OutputToEmit.Add(Out(Paused));
            await vm.TryTeamCommand.ExecuteAsync();
            Assert.Equal(["forge", "resume", "veille", "--events", "jsonl"], processes.Requests[^1].Arguments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
