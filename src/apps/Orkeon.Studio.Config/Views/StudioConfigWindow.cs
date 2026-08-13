using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Orkeon.Studio.Config.Presentation;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Validation;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
// Aliased under a distinct name: the bare name `Application` binds to the enclosing
// `Orkeon.Application` namespace here, and the alias marks the Terminal.Gui call sites.
using TerminalApp = Terminal.Gui.App.Application;

namespace Orkeon.Studio.Config.Views;

/// <summary>
/// The editor window: a section list on the left, the selected screen on the right, and
/// the actions along the bottom. It owns no rule of its own — every button hands off to
/// <see cref="ConfigEditorModel"/>, which hands off to <c>Orkeon.Studio.Core</c>.
/// </summary>
internal sealed class StudioConfigWindow : Window
{
    private readonly ConfigEditorModel _model;
    private readonly IDirectoryLister? _lister;
    private readonly List<SectionView> _sectionViews = [];
    private readonly FrameView _navigationFrame;
    private readonly ListView _navigation;
    private readonly View _content;
    private readonly Label _status;
    private int _currentSection;

    /// <summary>Builds the window over an editor session.</summary>
    public StudioConfigWindow(ConfigEditorModel model, IDirectoryLister? lister = null)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _lister = lister;

        Title = "Orkeon Studio — appsettings editor";

        _navigation = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        _navigationFrame = new FrameView
        {
            Title = "Sections",
            X = 0,
            Y = 0,
            Width = 30,
            Height = Dim.Fill(4),
        };
        _navigationFrame.Add(_navigation);

        _content = new View
        {
            X = Pos.Right(_navigationFrame),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(4),
        };

        _sectionViews.Add(new LlmSectionView(_model.Llm, _model.LlmProbe));
        _sectionViews.Add(new RateLimitingSectionView(_model.RateLimiting));
        _sectionViews.Add(new RagSectionView(_model.Rag));
        _sectionViews.Add(new MountsSectionView(_model.Mounts, _model.Directories, lister));
        _sectionViews.Add(new LoggingSectionView(_model.Logging));
        _sectionViews.Add(new LlmLoggingSectionView(_model.LlmLogging));
        _sectionViews.Add(new RawJsonSectionView(_model));

        foreach (var view in _sectionViews)
            _content.Add(view);

        _navigation.SetSource(new ObservableCollection<string>(
            _sectionViews.Select(view => view.Title ?? "").ToList()));
        _navigation.SelectedItem = 0;
        _navigation.ValueChanged += (_, _) => SelectSection(FormLayout.SelectedIndex(_navigation));

        _status = new Label { X = 1, Y = Pos.AnchorEnd(1), Width = Dim.Fill(1) };

        Add(_navigationFrame, _content, _status);
        AddActions();

        SelectSection(0);
        RefreshStatus("Ready.");
    }

    /// <summary>The section screens, in navigation order — the window's whole content.</summary>
    public IReadOnlyList<SectionView> Sections => _sectionViews;

    /// <summary>Index of the screen currently shown.</summary>
    public int CurrentSection => _currentSection;

    /// <summary>Shows the screen at <paramref name="index"/>, saving what the current one holds.</summary>
    public void SelectSection(int index)
    {
        if (index < 0 || index >= _sectionViews.Count)
            return;

        _sectionViews[_currentSection].Apply();
        _currentSection = index;

        for (var i = 0; i < _sectionViews.Count; i++)
            _sectionViews[i].Visible = i == index;

        // The raw view must show what the typed screens have just written into the document.
        if (_sectionViews[index] is RawJsonSectionView)
        {
            _model.ApplyForms();
            _sectionViews[index].Load();
        }
    }

    /// <summary>Reads every screen back into the document and validates it, without writing.</summary>
    /// <param name="scope">
    /// Why the document is being validated. The Save button passes
    /// <see cref="ValidationScope.Saving"/>, which turns findings the editor tolerates —
    /// an empty mount list — into blocking errors.
    /// </param>
    public SavePreflight Validate(ValidationScope scope = ValidationScope.Editing)
    {
        foreach (var view in _sectionViews)
            view.Apply();

        return _model.Preflight(scope);
    }

    /// <summary>Reloads every screen from the document (after a preset, or after opening a file).</summary>
    public void ReloadSections()
    {
        foreach (var view in _sectionViews)
            view.Load();
    }

    private void AddActions()
    {
        var open = new Button { X = 1, Y = Pos.AnchorEnd(3), Text = "Open…" };
        var save = new Button { X = Pos.Right(open) + 1, Y = Pos.AnchorEnd(3), Text = "Save…" };
        var validate = new Button { X = Pos.Right(save) + 1, Y = Pos.AnchorEnd(3), Text = "Validate" };
        var preset = new Button { X = Pos.Right(validate) + 1, Y = Pos.AnchorEnd(3), Text = "Preset…" };
        var diagnostic = new Button { X = Pos.Right(preset) + 1, Y = Pos.AnchorEnd(3), Text = "Diagnostic" };
        var quit = new Button { X = Pos.Right(diagnostic) + 1, Y = Pos.AnchorEnd(3), Text = "Quit" };

        open.Accepting += (_, _) => OpenFile();
        save.Accepting += (_, _) => SaveFile();
        validate.Accepting += (_, _) => ShowValidation();
        preset.Accepting += (_, _) => ApplyPreset();
        diagnostic.Accepting += (_, _) => RunDiagnostic();
        quit.Accepting += (_, _) => TerminalApp.RequestStop(this);

        Add(open, save, validate, preset, diagnostic, quit);
    }

    private void OpenFile()
    {
        // Files are offered alongside the folders: a settings file is often named for its
        // environment (appsettings.Development.json, crew.json), and a picker that could only
        // return a directory made those unopenable.
        using var picker = new DirectoryPickerDialog(
            _model.CurrentPath is { Length: > 0 } current ? Path.GetDirectoryName(current) : null,
            _lister,
            SettingsFileSearchPattern);

        if (picker.Show() is not { Length: > 0 } picked)
            return;

        // Standing in a folder rather than highlighting a file still means "the conventional
        // file here", which is what the action used to do unconditionally.
        var path = picked.EndsWith(SettingsFileExtension, StringComparison.OrdinalIgnoreCase)
            ? picked
            : Path.Combine(picked, AppSettingsDocument.FileName);

        var error = _model.OpenAsync(path).GetAwaiter().GetResult();

        if (error is not null)
        {
            MessageListDialog.ShowInfo("Open", error);
            return;
        }

        ReloadSections();
        RefreshStatus("Opened.");
    }

    /// <summary>Extension of the files the "Open…" picker offers.</summary>
    private const string SettingsFileExtension = ".json";

    /// <summary>Glob handed to the picker so any settings file can be opened, not just the conventional name.</summary>
    private const string SettingsFileSearchPattern = "*" + SettingsFileExtension;

    private void SaveFile()
    {
        var preflight = Validate(ValidationScope.Saving);

        if (preflight.HasBlockingErrors)
        {
            MessageListDialog.ShowInfo("Fix these before saving", preflight.Lines);
            RefreshStatus(MessageFormatter.Summarize(preflight.Messages));
            return;
        }

        if (preflight.HasWarnings
            && !MessageListDialog.Confirm("Validation warnings", preflight.Lines, "Save anyway", "Cancel"))
        {
            return;
        }

        using var location = new SaveLocationDialog(_model.CurrentPath);
        if (location.Show() is not { Length: > 0 } path)
            return;

        var error = _model.SaveAsync(path).GetAwaiter().GetResult();
        if (error is not null)
        {
            MessageListDialog.ShowInfo("Save failed", error);
            return;
        }

        ReloadSections();
        RefreshStatus(string.Create(CultureInfo.InvariantCulture, $"Saved to {path}."));
    }

    private void ShowValidation()
    {
        var preflight = Validate();
        MessageListDialog.ShowInfo("Validation", preflight.Lines);
        RefreshStatus(MessageFormatter.Summarize(preflight.Messages));
    }

    private void ApplyPreset()
    {
        using var dialog = new PresetDialog();
        if (dialog.Show() is not { } plan)
            return;

        var guidance = _model.ApplyPreset(plan);
        ReloadSections();

        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"Preset '{plan.Preset}' applied to the Llm section."),
        };
        lines.AddRange(guidance);

        MessageListDialog.ShowInfo("Preset applied", lines);
        RefreshStatus(string.Create(CultureInfo.InvariantCulture, $"Preset '{plan.Preset}' applied."));
    }

    private void RunDiagnostic()
    {
        var location = _model.LocateBinary();
        if (!location.Found)
        {
            var lines = new List<string> { location.Error ?? "`orkeon` was not found." };
            lines.Add("");
            lines.Add("Looked in:");
            lines.AddRange(location.ProbedPaths);

            MessageListDialog.ShowInfo("Diagnostic unavailable", lines);
            return;
        }

        RefreshStatus("Running `orkeon doctor --json`…");
        _ = ShowDoctorReportAsync();
    }

    [SuppressMessage("Design", "CA1031",
        Justification = "Fault barrier around a child-process run started from a button: the diagnostic " +
                        "must report whatever went wrong in its own dialog rather than tear down the UI.")]
    private async Task ShowDoctorReportAsync()
    {
        IReadOnlyList<string> lines;
        string status;

        try
        {
            var report = await _model.RunDoctorAsync().ConfigureAwait(false);
            lines = MessageFormatter.Format(report);
            status = report.HasFailures
                ? "Diagnostic: at least one check failed."
                : report.HasWarnings
                    ? "Diagnostic: warnings reported."
                    : "Diagnostic: all checks passed.";
        }
        catch (Exception ex)
        {
            lines = [string.Create(CultureInfo.InvariantCulture, $"`orkeon doctor` could not be run: {ex.Message}")];
            status = "Diagnostic failed.";
        }

        TerminalApp.Invoke(() =>
        {
            RefreshStatus(status);
            MessageListDialog.ShowInfo("Diagnostic", lines);
        });
    }

    private void RefreshStatus(string message)
    {
        var file = _model.CurrentPath is { Length: > 0 } path ? path : "(unsaved — no file yet)";
        var binary = _model.LocateBinary();
        var cli = binary.Found ? binary.Path! : "orkeon not found";

        _status.Text = string.Create(CultureInfo.InvariantCulture, $"{message}   File: {file}   CLI: {cli}");
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var view in _sectionViews)
                view.Dispose();

            _navigation.Dispose();
            _navigationFrame.Dispose();
            _content.Dispose();
            _status.Dispose();
        }

        base.Dispose(disposing);
    }
}
