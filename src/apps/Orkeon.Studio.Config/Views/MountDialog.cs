using System.Collections.ObjectModel;
using System.Globalization;
using Orkeon.Studio.Config.Presentation;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Validation;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
// Aliased under a distinct name: the bare name `Application` binds to the enclosing
// `Orkeon.Application` namespace here, and the alias marks the Terminal.Gui call sites.
using TerminalApp = Terminal.Gui.App.Application;

namespace Orkeon.Studio.Config.Views;

/// <summary>
/// Creates or edits one VFS mount (spec §4.5): physical path picked from a browser,
/// virtual path validated by the domain rule, rights from the closed <c>ro|rw|rwnd</c>
/// list, and optional sub-path overrides. Nothing is accepted until
/// <see cref="MountForm.Validate"/> — the boot-time checks — comes back clean.
/// </summary>
internal sealed class MountDialog : Window
{
    private readonly MountForm _form;
    private readonly IDirectoryLister? _lister;
    private readonly TextField _physicalPath;
    private readonly Label _physicalStatus;
    private readonly TextField _virtualPath;
    private readonly ListView _rights;
    private readonly ListView _overrides;
    private readonly TextField _overridePath;
    private readonly ListView _overrideRights;
    private readonly Label _problems;

    /// <summary>The mount the user accepted, or null when the dialog was cancelled.</summary>
    public MountDefinition? AcceptedMount { get; private set; }

    /// <summary>Builds the dialog over a form (empty for a creation, filled for an edit).</summary>
    public MountDialog(MountForm form, IDirectoryLister? lister = null)
    {
        _form = form ?? throw new ArgumentNullException(nameof(form));
        _lister = lister;

        Title = "Mount";
        X = Pos.Center();
        Y = Pos.Center();
        Width = Dim.Percent(90);
        Height = Dim.Percent(90);

        _physicalPath = FormLayout.AddField(this, 0, "Physical path", _form.PhysicalPath);
        var browse = new Button { X = FormLayout.Margin, Y = 1, Text = "Browse…" };
        var create = new Button { X = Pos.Right(browse) + 2, Y = 1, Text = "Create the folder" };
        _physicalStatus = FormLayout.AddText(this, 2, "");

        _virtualPath = FormLayout.AddField(this, 3, "Virtual path", _form.VirtualPath);
        FormLayout.AddNote(this, 4, string.Create(
            CultureInfo.InvariantCulture,
            $"Must start with '/'. Suggestions: {string.Join(", ", MountForm.VirtualPathSuggestions)}"));

        _rights = FormLayout.AddChoiceList(
            this, 5, MountForm.RightsChoices.Count, "Rights", MountForm.RightsChoices, _form.RightsChoiceIndex);

        var overridesTop = 5 + MountForm.RightsChoices.Count + 1;
        FormLayout.AddNote(this, overridesTop, "Sub-path rights overrides (optional):");
        _overrides = new ListView
        {
            X = FormLayout.Margin,
            Y = overridesTop + 1,
            Width = Dim.Fill(FormLayout.Margin),
            Height = 4,
        };
        Add(_overrides);

        _overridePath = FormLayout.AddField(this, overridesTop + 6, "Sub-path (relative)", "");
        _overrideRights = FormLayout.AddChoiceList(
            this, overridesTop + 7, MountForm.RightsChoices.Count, "Sub-path rights", MountForm.RightsChoices, 0);

        var buttonsTop = overridesTop + 8 + MountForm.RightsChoices.Count;
        var addOverride = new Button { X = FormLayout.Margin, Y = buttonsTop, Text = "Add override" };
        var removeOverride = new Button { X = Pos.Right(addOverride) + 2, Y = buttonsTop, Text = "Remove override" };

        _problems = FormLayout.AddText(this, buttonsTop + 2, "");

        var accept = new Button { X = FormLayout.Margin, Y = Pos.AnchorEnd(1), Text = "OK", IsDefault = true };
        var cancel = new Button { X = Pos.Right(accept) + 2, Y = Pos.AnchorEnd(1), Text = "Cancel" };

        browse.Accepting += (_, _) => Browse();
        create.Accepting += (_, _) => CreateFolder();
        addOverride.Accepting += (_, _) => AddOverride();
        removeOverride.Accepting += (_, _) => RemoveOverride();
        accept.Accepting += (_, _) => Accept();
        cancel.Accepting += (_, _) => TerminalApp.RequestStop(this);

        _physicalPath.TextChanged += (_, _) => RefreshPhysicalStatus();

        Add(browse, create, addOverride, removeOverride, accept, cancel);
        RefreshOverrides();
        RefreshPhysicalStatus();
    }

    /// <summary>Runs the dialog and returns the accepted mount, or null.</summary>
    public MountDefinition? Show()
    {
        TerminalApp.Run(this);
        return AcceptedMount;
    }

    private void ReadForm()
    {
        _form.PhysicalPath = _physicalPath.Text ?? "";
        _form.VirtualPath = _virtualPath.Text ?? "";
        _form.RightsChoiceIndex = FormLayout.SelectedIndex(_rights);
    }

    private void Browse()
    {
        ReadForm();

        using var picker = new DirectoryPickerDialog(_form.PhysicalPath, _lister);
        if (picker.Show() is { Length: > 0 } picked)
        {
            _physicalPath.Text = picked;
            _form.PhysicalPath = picked;
            RefreshPhysicalStatus();
        }
    }

    private void CreateFolder()
    {
        ReadForm();

        _problems.Text = _form.TryCreatePhysicalDirectory(out var error)
            ? "Folder created."
            : error;

        RefreshPhysicalStatus();
    }

    private void AddOverride()
    {
        var rights = MountRightsTokens.Choices[Math.Clamp(
            FormLayout.SelectedIndex(_overrideRights), 0, MountRightsTokens.Choices.Count - 1)].Rights;

        if (!_form.TryAddOverride(_overridePath.Text, rights, out var error))
        {
            _problems.Text = error;
            return;
        }

        _overridePath.Text = "";
        _problems.Text = "";
        RefreshOverrides();
    }

    private void RemoveOverride()
    {
        _form.RemoveOverrideAt(FormLayout.SelectedIndex(_overrides));
        RefreshOverrides();
    }

    private void Accept()
    {
        ReadForm();

        var messages = _form.Validate();
        var errors = messages.Where(message => message.Severity == ValidationSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            _problems.Text = string.Join("  |  ", errors.Select(message => message.Text));
            return;
        }

        if (!_form.TryBuild(out var definition, out _))
            return;

        AcceptedMount = definition;
        TerminalApp.RequestStop(this);
    }

    private void RefreshOverrides()
    {
        var items = _form.Overrides.Count > 0
            ? _form.Overrides.Select(item => item.Display).ToList()
            : ["(none)"];

        _overrides.SetSource(new ObservableCollection<string>(items));
        _overrides.SelectedItem = 0;
    }

    private void RefreshPhysicalStatus()
    {
        _form.PhysicalPath = _physicalPath.Text ?? "";

        if (string.IsNullOrWhiteSpace(_form.PhysicalPath))
        {
            _physicalStatus.Text = "Pick the folder this mount exposes.";
            return;
        }

        _physicalStatus.Text = _form.PhysicalPathExists
            ? "The folder exists."
            : "This folder does not exist — the runtime rejects such a mount. Use 'Create the folder'.";
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _physicalPath.Dispose();
            _physicalStatus.Dispose();
            _virtualPath.Dispose();
            _rights.Dispose();
            _overrides.Dispose();
            _overridePath.Dispose();
            _overrideRights.Dispose();
            _problems.Dispose();
        }

        base.Dispose(disposing);
    }
}
