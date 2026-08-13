using System.Collections.ObjectModel;
using Orkeon.Studio.Core.FileSystem;
using TerminalApp = Terminal.Gui.App.Application;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Orkeon.Studio.Run.Views;

/// <summary>
/// The mount form of SPEC §4.5, in its launcher incarnation: physical path — browsed, and
/// creatable when it does not exist yet, as in the appsettings editor — virtual path, a closed
/// rights list, and the optional per-sub-path overrides. Nothing here spells the Docker-style
/// mount string by hand: <see cref="MountDefinition"/> serializes it,
/// <see cref="SubPathOverrideText"/> reads the overrides field, and the domain parser is what
/// accepts or rejects the result.
/// </summary>
internal sealed class MountFormDialog : Window
{
    private readonly IDirectoryProbe _directories;
    private readonly IDirectoryLister? _lister;
    private readonly TextField _physical;
    private readonly Label _physicalStatus;
    private readonly TextField _virtual;
    private readonly ListView _rights;
    private readonly TextView _overrides;
    private readonly Label _error;

    internal MountFormDialog(
        MountDefinition? existing,
        IDirectoryProbe? directories = null,
        IDirectoryLister? lister = null)
    {
        _directories = directories ?? PhysicalDirectoryProbe.Instance;
        _lister = lister;

        Title = existing is null ? "Add a launch mount" : "Edit launch mount";
        X = Pos.Center();
        Y = Pos.Center();
        Width = Dim.Percent(90);
        Height = 21;

        var physicalLabel = new Label { X = 1, Y = 0, Text = "Physical folder :" };
        _physical = new TextField
        {
            X = 19,
            Y = 0,
            Width = Dim.Fill(1),
            Text = existing?.PhysicalPath ?? string.Empty,
        };

        var browse = new Button { X = 19, Y = 1, Text = "Browse…" };
        var create = new Button { X = Pos.Right(browse) + 2, Y = 1, Text = "Create folder" };
        _physicalStatus = new Label { X = 19, Y = 2, Width = Dim.Fill(1), Text = string.Empty };

        var virtualLabel = new Label { X = 1, Y = 3, Text = "Virtual path    :" };
        _virtual = new TextField
        {
            X = 19,
            Y = 3,
            Width = Dim.Fill(1),
            Text = existing?.VirtualPath ?? MountDefinition.SuggestedVirtualPaths[0],
        };

        var suggestions = new Label
        {
            X = 19,
            Y = 4,
            Width = Dim.Fill(1),
            Text = "Suggestions: " + string.Join(", ", MountDefinition.SuggestedVirtualPaths),
        };

        var rightsLabel = new Label { X = 1, Y = 5, Text = "Rights          :" };
        _rights = new ListView { X = 19, Y = 5, Width = Dim.Fill(1), Height = MountRightsTokens.Choices.Count };
        _rights.SetSource(new ObservableCollection<string>(
            MountRightsTokens.Choices.Select(choice => $"{choice.Token} — {choice.Label}")));
        _rights.SelectedItem = MountRightsTokens.IndexOf(existing?.Rights ?? MountRights.ReadOnly);

        var overridesLabel = new Label
        {
            X = 1,
            Y = 5 + MountRightsTokens.Choices.Count,
            Width = Dim.Fill(1),
            Text = "Sub-path overrides, one 'relative/path:rights' per line (optional):",
        };
        _overrides = new TextView
        {
            X = 1,
            Y = 6 + MountRightsTokens.Choices.Count,
            Width = Dim.Fill(1),
            Height = 3,
            Multiline = true,
            Text = SubPathOverrideText.Format(existing?.Overrides),
        };

        _error = new Label { X = 1, Y = Pos.AnchorEnd(2), Width = Dim.Fill(1), Text = string.Empty };

        var accept = new Button { X = 1, Y = Pos.AnchorEnd(1), Text = "OK", IsDefault = true };
        var cancel = new Button { X = Pos.Right(accept) + 2, Y = Pos.AnchorEnd(1), Text = "Cancel" };

        browse.Accepting += (_, _) => Browse();
        create.Accepting += (_, _) => CreateFolder();
        accept.Accepting += (_, _) => TryAccept();
        cancel.Accepting += (_, _) =>
        {
            Mount = null;
            TerminalApp.RequestStop(this);
        };

        _physical.TextChanged += (_, _) => RefreshPhysicalStatus();

        Add(physicalLabel, _physical, browse, create, _physicalStatus, virtualLabel, _virtual, suggestions,
            rightsLabel, _rights, overridesLabel, _overrides, _error, accept, cancel);

        RefreshPhysicalStatus();
    }

    /// <summary>The mount the user described, or null when the form was cancelled.</summary>
    internal MountDefinition? Mount { get; private set; }

    /// <summary>Runs the form modally; returns the mount, or null on cancel.</summary>
    internal static MountDefinition? Show(
        MountDefinition? existing = null,
        IDirectoryProbe? directories = null,
        IDirectoryLister? lister = null)
    {
        using var dialog = new MountFormDialog(existing, directories, lister);
        TerminalApp.Run(dialog, errorHandler: null);
        return dialog.Mount;
    }

    /// <summary>Test-only: the line under the physical path field.</summary>
    internal string PhysicalStatusText => _physicalStatus.Text;

    /// <summary>Test-only: the error line under the form.</summary>
    internal string ErrorText => _error.Text;

    /// <summary>Test-only: fills the form as the user would.</summary>
    internal void FillForTest(string physical, string virtualPath, string? overrides = null)
    {
        _physical.Text = physical;
        _virtual.Text = virtualPath;
        _overrides.Text = overrides ?? string.Empty;
        RefreshPhysicalStatus();
    }

    /// <summary>Test-only: presses "Create folder".</summary>
    internal void CreateFolderForTest() => CreateFolder();

    /// <summary>Test-only: presses "OK".</summary>
    internal void AcceptForTest() => TryAccept();

    private void Browse()
    {
        using var picker = new DirectoryPickerDialog(NullIfBlank(_physical.Text), _lister);
        if (picker.Show() is { Length: > 0 } picked)
        {
            _physical.Text = picked;
            RefreshPhysicalStatus();
        }
    }

    private void CreateFolder()
    {
        _error.Text = _directories.TryCreate(_physical.Text, out var error) ? "Folder created." : error;
        RefreshPhysicalStatus();
    }

    private void TryAccept()
    {
        if (!SubPathOverrideText.TryParse(_overrides.Text, out var overrides, out var overrideError))
        {
            _error.Text = overrideError!;
            return;
        }

        var candidate = new MountDefinition
        {
            PhysicalPath = (_physical.Text ?? string.Empty).Trim(),
            VirtualPath = (_virtual.Text ?? string.Empty).Trim(),
            Rights = MountRightsTokens.At(_rights.SelectedItem ?? 0),
            Overrides = overrides,
        };

        // The authority is the domain parser the runtime itself uses, reached by round-tripping
        // the string this form serializes.
        if (!MountDefinition.TryParse(candidate.ToMountString(), out _, out var error))
        {
            _error.Text = error ?? "This mount is not valid.";
            return;
        }

        Mount = candidate;
        TerminalApp.RequestStop(this);
    }

    private void RefreshPhysicalStatus()
    {
        var path = NullIfBlank(_physical.Text);

        _physicalStatus.Text = path is null
            ? "Pick the folder this mount exposes."
            : _directories.Exists(path)
                ? "The folder exists."
                : "This folder does not exist — the runtime rejects such a mount. Use 'Create folder'.";
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Terminal.Gui's base View.Dispose also disposes the Add()-ed subviews; their
            // IsDisposed guard makes these explicit calls idempotent no-ops, and satisfies the
            // "owned IDisposable field" rule the repo builds with.
            _physical.Dispose();
            _physicalStatus.Dispose();
            _virtual.Dispose();
            _rights.Dispose();
            _overrides.Dispose();
            _error.Dispose();
        }

        base.Dispose(disposing);
    }
}
