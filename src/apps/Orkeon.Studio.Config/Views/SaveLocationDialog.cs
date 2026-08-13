using System.Collections.ObjectModel;
using Orkeon.Studio.Config.Presentation;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
// Aliased under a distinct name: the bare name `Application` binds to the enclosing
// `Orkeon.Application` namespace here, and the alias marks the Terminal.Gui call sites.
using TerminalApp = Terminal.Gui.App.Application;

namespace Orkeon.Studio.Config.Views;

/// <summary>
/// Where to write the file. The two targets are the per-user global file and a path the
/// user picks; the full resolution chain is shown next to them so it is clear which file a
/// later <c>orkeon run</c> will actually load.
/// </summary>
internal sealed class SaveLocationDialog : Window
{
    private static readonly string[] TargetChoices =
    [
        "Global per-user file (written by `orkeon init`)",
        "A path I choose",
    ];

    private readonly SaveLocationModel _model = new();
    private readonly ListView _targets;
    private readonly Label _globalPath;
    private readonly TextField _customPath;
    private readonly Label _problem;

    /// <summary>The resolved absolute path, or null when the dialog was cancelled.</summary>
    public string? AcceptedPath { get; private set; }

    /// <summary>Builds the chooser, pre-selecting the file currently open.</summary>
    public SaveLocationDialog(string? currentPath)
    {
        _model.SelectCurrentFile(currentPath);

        Title = "Save location";
        X = Pos.Center();
        Y = Pos.Center();
        Width = Dim.Percent(90);
        Height = Dim.Percent(80);

        _targets = FormLayout.AddChoiceList(
            this, 0, TargetChoices.Length, "Target", TargetChoices,
            _model.Mode == SaveLocationMode.Global ? 0 : 1);

        _globalPath = FormLayout.AddText(this, 3, GlobalPathLine());
        _customPath = FormLayout.AddField(this, 4, "Path", _model.CustomPath);

        var browse = new Button { X = FormLayout.Margin, Y = 5, Text = "Browse…" };
        browse.Accepting += (_, _) => Browse();

        FormLayout.AddNote(this, 7, "Resolution chain used by the runtime (first match wins):");
        var chain = new ListView
        {
            X = FormLayout.Margin,
            Y = 8,
            Width = Dim.Fill(FormLayout.Margin),
            Height = Dim.Fill(4),
        };
        chain.SetSource(new ObservableCollection<string>(SaveLocationModel.ResolutionChainLines));
        chain.CanFocus = false;

        _problem = new Label { X = FormLayout.Margin, Y = Pos.AnchorEnd(3), Width = Dim.Fill(FormLayout.Margin) };

        var accept = new Button { X = FormLayout.Margin, Y = Pos.AnchorEnd(1), Text = "Use this location", IsDefault = true };
        var cancel = new Button { X = Pos.Right(accept) + 2, Y = Pos.AnchorEnd(1), Text = "Cancel" };

        accept.Accepting += (_, _) => Accept();
        cancel.Accepting += (_, _) => TerminalApp.RequestStop(this);

        Add(browse, chain, _problem, accept, cancel);
    }

    /// <summary>Runs the chooser and returns the resolved path, or null.</summary>
    public string? Show()
    {
        TerminalApp.Run(this);
        return AcceptedPath;
    }

    private void Accept()
    {
        _model.Mode = FormLayout.SelectedIndex(_targets) == 0 ? SaveLocationMode.Global : SaveLocationMode.CustomPath;
        _model.CustomPath = _customPath.Text ?? "";

        if (!_model.TryResolve(out var path, out var error))
        {
            _problem.Text = error;
            return;
        }

        AcceptedPath = path;
        TerminalApp.RequestStop(this);
    }

    private void Browse()
    {
        using var picker = new DirectoryPickerDialog(_customPath.Text);
        if (picker.Show() is { Length: > 0 } picked)
        {
            // A directory receives an appsettings.json, as `orkeon init --path` does.
            _customPath.Text = picked;
            _targets.SelectedItem = 1;
        }
    }

    private string GlobalPathLine() =>
        _model.GlobalPath is { Length: > 0 } path
            ? "Global file: " + path
            : "Global file unavailable: " + (_model.GlobalPathError ?? "no per-user configuration directory.");

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _targets.Dispose();
            _globalPath.Dispose();
            _customPath.Dispose();
            _problem.Dispose();
        }

        base.Dispose(disposing);
    }
}
