using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// STUDIO-47: files and folders dropped on the step-1 need. Each existing path is inserted at
/// the caret, quoted; a dropped folder — or a dropped file's own folder — becomes a folder the
/// team reads, bound to that real folder in the Folders step (STUDIO-46).
/// </summary>
public partial class CreateTeamWizardTests
{
    private static FakeDiskEntryProbe Disk() =>
        new FakeDiskEntryProbe()
            .WithDirectories("/data/pdf", "/data/inpdf", "/data/notes", "/data/My PDFs")
            .WithFiles("/data/inpdf/a.pdf", "/data/notes/a.md", "/data/notes/b.md");

    /// <summary>What the shell does with a declare request: the folder bound under the row's name.</summary>
    private static List<(string Target, MountRights Rights, string Folder)> ActLikeTheShell(CreateTeamViewModel vm)
    {
        var requests = new List<(string, MountRights, string)>();
        vm.DeclareFolderRequested += (_, e) =>
        {
            requests.Add((e.TargetVirtualPath, e.Rights, e.Folder));
            vm.BindTeamMount(e.TargetVirtualPath, Folder(e.Folder, e.TargetVirtualPath, e.Rights));
        };
        return requests;
    }

    [Fact]
    public async Task Dropping_a_folder_inserts_its_quoted_path_at_the_caret_and_makes_it_a_folder_the_team_reads()
    {
        var (vm, processes, _) = Build(diskEntries: Disk());
        var requests = ActLikeTheShell(vm);
        vm.Need = "Résumer les PDF de chaque semaine";

        var caret = vm.DropPaths(["/data/pdf"], "Résumer les PDF".Length);

        Assert.Equal("Résumer les PDF \"/data/pdf\" de chaque semaine", vm.Need);
        Assert.Equal("Résumer les PDF \"/data/pdf\"".Length, caret);

        ScriptFoldersStep(vm, processes, [("/output", "output")], wizard =>
        {
            Assert.Equal(["/output", "/pdf"], wizard.FolderRows.Select(r => r.VirtualPath));
            var dropped = wizard.FolderRows[1];
            Assert.True(dropped.IsInput);
            Assert.Equal("/data/pdf", dropped.Directory);
            Assert.Equal("/data/pdf", dropped.FolderLabel);
            Assert.True(wizard.CanConfirmFolders);
            wizard.ConfirmFoldersCommand.Execute(null);
        }, PdfBlueprint("/output"), Paused);

        await Compose(vm);

        Assert.Equal([("/pdf", MountRights.ReadOnly, "/data/pdf")], requests);
        Assert.Equal([("/output", (string?)null), ("/pdf", "/data/pdf")], Confirmed(processes));
        Assert.Contains("/data/pdf:/pdf:ro", vm.TeamMounts);
    }

    [Fact]
    public async Task Dropping_a_file_inserts_its_path_and_proposes_its_folder_under_the_name_the_request_gave_it()
    {
        var (vm, processes, _) = Build(diskEntries: Disk());
        vm.Need = "Convertir";

        var caret = vm.DropPaths(["/data/inpdf/a.pdf"], vm.Need.Length);

        Assert.Equal("Convertir \"/data/inpdf/a.pdf\" ", vm.Need);
        Assert.Equal(vm.Need.Length, caret);

        // The forge read /inpdf in the request: the dropped file's folder answers that row
        // rather than add a second one beside it.
        ScriptFoldersStep(vm, processes, PdfFolders, wizard =>
        {
            Assert.Equal(["/inpdf", "/outmd"], wizard.FolderRows.Select(r => r.VirtualPath));
            Assert.Equal("/data/inpdf", wizard.FolderRows[0].Directory);
            Assert.True(wizard.FolderRows[1].IsInsideTeam);
            wizard.ConfirmFoldersCommand.Execute(null);
        }, PdfBlueprint(), Paused);

        await Compose(vm);

        Assert.Equal([("/inpdf", "/data/inpdf"), ("/outmd", null)], Confirmed(processes));
    }

    [Fact]
    public async Task Several_dropped_items_are_all_inserted_and_each_folder_is_proposed_once()
    {
        var (vm, processes, _) = Build(diskEntries: Disk());
        vm.Need = "";

        var caret = vm.DropPaths(["/data/pdf", "/data/notes/a.md", "/data/notes/b.md", "/data/My PDFs"], 0);

        Assert.Equal("\"/data/pdf\" \"/data/notes/a.md\" \"/data/notes/b.md\" \"/data/My PDFs\" ", vm.Need);
        Assert.Equal(vm.Need.Length, caret);

        ScriptFoldersStep(vm, processes, [("/output", "output")], wizard =>
        {
            Assert.Equal(["/output", "/pdf", "/notes", "/my-pdfs"], wizard.FolderRows.Select(r => r.VirtualPath));
            Assert.Equal([null, "/data/pdf", "/data/notes", "/data/My PDFs"], wizard.FolderRows.Select(r => r.Directory));
            wizard.ConfirmFoldersCommand.Execute(null);
        }, PdfBlueprint("/output"), Paused);

        await Compose(vm);

        Assert.Equal(4, Confirmed(processes).Count);
    }

    [Fact]
    public void A_path_that_does_not_exist_is_ignored()
    {
        var (vm, _, _) = Build(diskEntries: Disk());
        vm.Need = "Lire";

        Assert.Equal(2, vm.DropPaths(["/nowhere"], 2));
        Assert.Equal("Lire", vm.Need);

        var caret = vm.DropPaths(["/nowhere", "/data/pdf", ""], 4);
        Assert.Equal("Lire \"/data/pdf\" ", vm.Need);
        Assert.Equal(vm.Need.Length, caret);
    }

    [Fact]
    public async Task A_dropped_folder_answers_the_default_input_rather_than_add_a_second_one()
    {
        var (vm, processes, _) = Build(diskEntries: Disk());
        vm.Need = "Résumer";
        vm.DropPaths(["/data/pdf"], 0);

        ScriptFoldersStep(vm, processes, [("/workspace", "input"), ("/output", "output")], wizard =>
        {
            Assert.Equal(["/workspace", "/output"], wizard.FolderRows.Select(r => r.VirtualPath));
            Assert.Equal("/data/pdf", wizard.FolderRows[0].Directory);
            wizard.ConfirmFoldersCommand.Execute(null);
        }, PdfBlueprint("/output"), Paused);

        await Compose(vm);

        Assert.Equal([("/workspace", "/data/pdf"), ("/output", null)], Confirmed(processes));
    }

    [Fact]
    public async Task A_path_taken_out_of_the_need_before_composing_is_no_longer_proposed()
    {
        var (vm, processes, _) = Build(diskEntries: Disk());
        vm.Need = "Résumer";
        vm.DropPaths(["/data/pdf", "/data/notes"], vm.Need.Length);
        vm.Need = vm.Need.Replace("\"/data/pdf\" ", "", StringComparison.Ordinal);

        ScriptFoldersStep(vm, processes, [("/output", "output")], wizard =>
        {
            Assert.Equal(["/output", "/notes"], wizard.FolderRows.Select(r => r.VirtualPath));
            wizard.ConfirmFoldersCommand.Execute(null);
        }, PdfBlueprint("/output"), Paused);

        await Compose(vm);
    }

    [Fact]
    public async Task Starting_over_forgets_the_dropped_folders()
    {
        var (vm, processes, _) = Build(diskEntries: Disk());
        vm.Need = "Résumer";
        vm.DropPaths(["/data/pdf"], 0);
        ScriptFoldersStep(vm, processes, [("/output", "output")], wizard => wizard.ConfirmFoldersCommand.Execute(null),
            PdfBlueprint("/output"), Paused);
        await Compose(vm);
        Assert.Equal(2, vm.Step);

        vm.RestartCommand.Execute(null);
        Assert.Equal(1, vm.Step);
        // The same words typed by hand: nothing was dropped on this creation.
        vm.Need = "Résumer \"/data/pdf\"";
        ScriptFoldersStep(vm, processes, [("/output", "output")], wizard =>
        {
            Assert.Equal(["/output"], wizard.FolderRows.Select(r => r.VirtualPath));
            wizard.ConfirmFoldersCommand.Execute(null);
        }, PdfBlueprint("/output"), Paused);

        await Compose(vm);
    }

    /// <summary>
    /// Through the shell: a dropped folder is a disk pick without the picker — declared in the
    /// settings under its row's name and rights, saved, and bound behind the row, so the run that
    /// follows the adoption is not refused for an undeclared folder.
    /// </summary>
    [Fact]
    public async Task A_dropped_folder_is_declared_in_the_settings_and_bound_behind_its_row()
    {
        var dropped = Path.Combine(Path.GetTempPath(), "orkeon-drop-" + Guid.NewGuid().ToString("N"), "Factures");
        Directory.CreateDirectory(dropped);
        try
        {
            var forge = new FakeProcessLauncher();
            var (shell, store, picker) = Shell(new FakeDirectoryProbe(dropped), forge: forge);
            shell.Settings.Profiles.CommitEdit(
                new Orkeon.Studio.Core.Profiles.ModelProfile { Name = "Local", Provider = "Ollama", Model = "qwen2.5:14b", BaseUrl = "http://localhost:11434/v1" },
                previousName: null);
            shell.Settings.Profiles.StudioProfileName = "Local";
            var wizard = shell.CreateTeam;
            wizard.Need = "Classer les factures";

            wizard.DropPaths([dropped], wizard.Need.Length);

            ScriptFoldersStep(wizard, forge, [("/output", "output")], w =>
            {
                Assert.Equal(dropped, w.FolderRows.Single(r => r.VirtualPath == "/factures").Directory);
                w.ConfirmFoldersCommand.Execute(null);
            }, PdfBlueprint("/output"), Paused);
            await wizard.ComposeCommand.ExecuteAsync();

            Assert.Empty(picker.Prompts);
            var declared = Assert.Single(shell.Config.Mounts.CurrentMountStrings);
            Assert.Equal($"{dropped}:/factures:ro", MountDefinition.Parse(declared).WithoutId().ToMountString());
            Assert.Contains(declared, wizard.TeamMounts);
            Assert.NotEmpty(store.SavedPaths);
            Assert.False(wizard.MountRows.Single(r => r.VirtualPath == "/factures").IsUndeclared);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(dropped)!, recursive: true);
        }
    }
}
