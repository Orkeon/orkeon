using Orkeon.Studio.Config.Presentation;
using Orkeon.Studio.Config.Tests.Doubles;
using Orkeon.Studio.Config.Views;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Config.Tests.Views;

public class StudioConfigWindowTests
{
    private static ConfigEditorModel CreateModel()
    {
        var runner = new OrkeonProcessRunner(
            new FakeProcessLauncher(),
            new OrkeonBinaryLocator(new FakeExecutableProbe(), ["orkeon"]));

        return new ConfigEditorModel(new FakeDirectoryProbe("/data/in"), runner, new FakeLlmEndpointProbe());
    }

    [Fact]
    public void The_window_opens_on_the_llm_screen_with_every_section_available()
    {
        using var window = new StudioConfigWindow(CreateModel());

        var titles = window.Sections.Select(section => section.Title).ToList();

        Assert.Equal(
            ["LLM", "Rate limiting", "RAG", MountEditorModel.SectionTitle, "Logging", "LLM logging", "Raw JSON (read-only)"],
            titles);
        Assert.Equal(0, window.CurrentSection);
        Assert.True(window.Sections[0].Visible);
        Assert.False(window.Sections[1].Visible);
    }

    [Fact]
    public void Selecting_a_section_shows_it_and_hides_the_others()
    {
        using var window = new StudioConfigWindow(CreateModel());

        window.SelectSection(2);

        Assert.Equal(2, window.CurrentSection);
        Assert.True(window.Sections[2].Visible);
        Assert.All(window.Sections.Where((_, index) => index != 2), section => Assert.False(section.Visible));
    }

    [Fact]
    public void An_index_outside_the_section_list_is_ignored()
    {
        using var window = new StudioConfigWindow(CreateModel());

        window.SelectSection(99);

        Assert.Equal(0, window.CurrentSection);
    }

    [Fact]
    public void Validating_reads_every_screen_back_into_the_document()
    {
        var model = CreateModel();
        using var window = new StudioConfigWindow(model);

        model.Llm.Model = "llama3";
        model.Llm.BaseUrl = "http://localhost:11434";
        window.ReloadSections();

        var preflight = window.Validate();

        Assert.False(preflight.HasBlockingErrors);
        Assert.Equal("llama3", model.Document.Llm.Model);
    }

    [Fact]
    public void Validating_an_empty_document_surfaces_the_win01_warning()
    {
        using var window = new StudioConfigWindow(CreateModel());

        var preflight = window.Validate();

        Assert.Contains(preflight.Messages, message => message.Code == ValidationCodes.LlmSectionMissing);
    }

    [Fact]
    public void Opening_the_raw_screen_shows_what_the_typed_screens_have_written()
    {
        var model = CreateModel();
        using var window = new StudioConfigWindow(model);

        model.Llm.Model = "llama3";
        window.ReloadSections();
        window.SelectSection(window.Sections.Count - 1);

        Assert.Contains("llama3", model.RawJson);
    }

    [Fact]
    public void The_mount_screen_is_built_over_the_session_mount_list()
    {
        var model = CreateModel();
        model.Mounts.Add(new MountDefinition { PhysicalPath = "/data/in", VirtualPath = "/workspace" });

        using var window = new StudioConfigWindow(model, new FakeDirectoryLister().WithDirectory("/data/in"));
        window.ReloadSections();

        Assert.Single(model.Mounts.Rows);
    }

    [Fact]
    public void The_save_scope_blocks_an_empty_mount_list_that_the_edit_scope_only_warns_about()
    {
        using var window = new StudioConfigWindow(CreateModel());

        var editing = window.Validate();
        var saving = window.Validate(ValidationScope.Saving);

        Assert.False(editing.HasBlockingErrors);
        Assert.True(saving.HasBlockingErrors);
        Assert.Contains(
            saving.Messages,
            message => message.Code == ValidationCodes.MountsEmpty
                && message.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void The_open_picker_offers_the_json_files_of_the_folder_it_is_standing_in()
    {
        // The action used to return a folder and assume the conventional file name, which made
        // an appsettings.Development.json — or any settings file named for its crew — unopenable.
        var tree = new FakeDirectoryLister()
            .WithDirectory("/data/in")
            .WithFile("/data/in/appsettings.Development.json")
            .WithFile("/data/in/notes.txt");

        using var picker = new DirectoryPickerDialog("/data/in", tree, "*.json");

        Assert.Contains("appsettings.Development.json", picker.EntriesForTest);
        Assert.DoesNotContain("notes.txt", picker.EntriesForTest);
    }
}
