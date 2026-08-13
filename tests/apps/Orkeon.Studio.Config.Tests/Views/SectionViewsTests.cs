using Orkeon.Studio.Config.Presentation;
using Orkeon.Studio.Config.Tests.Doubles;
using Orkeon.Studio.Config.Views;
using Orkeon.Studio.Core.FileSystem;

namespace Orkeon.Studio.Config.Tests.Views;

/// <summary>
/// The screens are built and driven without a terminal: Terminal.Gui views can be
/// constructed outside <c>Application.Init</c>, which is how
/// tests/cli/Orkeon.Cli.TerminalGui.Tests covers its own panes.
/// </summary>
public class SectionViewsTests
{
    [Fact]
    public void The_llm_screen_carries_the_form_values_both_ways()
    {
        var form = new LlmForm();
        using var view = new LlmSectionView(form, new FakeLlmEndpointProbe());

        form.Model = "llama3";
        form.BaseUrl = "http://localhost:11434";
        form.MaxTokens = "512";
        view.Load();

        // Clearing the form proves the values are read back from the widgets, not kept.
        form.Model = "";
        form.BaseUrl = "";
        form.MaxTokens = "";
        view.Apply();

        Assert.Equal("llama3", form.Model);
        Assert.Equal("http://localhost:11434", form.BaseUrl);
        Assert.Equal("512", form.MaxTokens);
    }

    [Fact]
    public void The_rate_limiting_screen_carries_the_form_values_both_ways()
    {
        var form = new RateLimitingForm();
        using var view = new RateLimitingSectionView(form);

        form.QueueLimit = "42";
        view.Load();

        form.QueueLimit = "";
        view.Apply();

        Assert.Equal("42", form.QueueLimit);
    }

    [Fact]
    public void The_rag_screen_keeps_the_profile_selection_and_the_tri_state_switches()
    {
        var form = new RagForm();
        using var view = new RagSectionView(form);

        form.Profile = "corrective";
        form.HybridRetrievalEnabled = false;
        form.CorrectiveMaxIterations = "3";
        view.Load();

        form.Profile = null;
        form.HybridRetrievalEnabled = null;
        form.CorrectiveMaxIterations = "";
        view.Apply();

        Assert.Equal("corrective", form.Profile);
        Assert.Equal(false, form.HybridRetrievalEnabled);
        Assert.Equal("3", form.CorrectiveMaxIterations);
    }

    [Fact]
    public void The_logging_screen_keeps_the_default_level_and_lists_the_categories()
    {
        var form = new LoggingForm();
        Assert.True(form.TryAddCategory("Microsoft", "Warning", out _));

        using var view = new LoggingSectionView(form);

        form.DefaultLevel = "Warning";
        view.Load();

        form.DefaultLevel = null;
        view.Apply();

        Assert.Equal("Warning", form.DefaultLevel);

        // The rows are edited through the dialog, so Apply must leave them alone.
        Assert.Equal("Microsoft = Warning", Assert.Single(form.Categories).Display);
    }

    [Fact]
    public void A_log_category_dialog_opens_on_an_existing_category_without_a_terminal()
    {
        using var dialog = new LogCategoryDialog("Microsoft", "Warning");

        Assert.Null(dialog.AcceptedCategory);
    }

    [Fact]
    public void The_llm_logging_screen_keeps_its_switches()
    {
        var form = new LlmLoggingForm();
        using var view = new LlmLoggingSectionView(form);

        form.FullEmbeddingLog = true;
        form.MaxBodyLengthChars = "4096";
        view.Load();

        form.FullEmbeddingLog = null;
        form.MaxBodyLengthChars = "";
        view.Apply();

        Assert.Equal(true, form.FullEmbeddingLog);
        Assert.Equal("4096", form.MaxBodyLengthChars);
    }

    [Fact]
    public void The_mounts_screen_lists_what_the_model_holds()
    {
        var model = new MountEditorModel(new FakeDirectoryProbe("/data/in"));
        model.Add(new MountDefinition { PhysicalPath = "/data/in", VirtualPath = "/workspace" });

        using var view = new MountsSectionView(model, new FakeDirectoryProbe("/data/in"));
        view.Load();

        Assert.Equal(MountEditorModel.SectionTitle, view.Title);
        Assert.Single(model.Rows);
    }

    [Fact]
    public void A_mount_dialog_opens_on_an_existing_mount_without_a_terminal()
    {
        var form = MountForm.FromDefinition(
            MountDefinition.Parse("/data/in:/workspace:rw"),
            new FakeDirectoryProbe("/data/in"));

        using var dialog = new MountDialog(form, new FakeDirectoryLister().WithDirectory("/data/in"));

        Assert.Null(dialog.AcceptedMount);
    }

    [Fact]
    public void The_preset_dialog_opens_with_no_plan_chosen()
    {
        using var dialog = new PresetDialog();

        Assert.Null(dialog.AcceptedPlan);
    }

    [Fact]
    public void The_save_location_dialog_opens_on_the_file_being_edited()
    {
        using var dialog = new SaveLocationDialog("/srv/crew/appsettings.json");

        Assert.Null(dialog.AcceptedPath);
    }

    [Fact]
    public void The_message_dialog_reports_no_choice_until_a_button_is_pressed()
    {
        using var dialog = new MessageListDialog("Validation", ["one", "two"], ["Save anyway", "Cancel"]);

        Assert.Equal(-1, dialog.ChoiceIndex);
    }
}
