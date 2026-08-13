using System.Collections.ObjectModel;
using Orkeon.Studio.Core.FileSystem;
using Terminal.Gui.App;
using TerminalApp = Terminal.Gui.App.Application;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Orkeon.Studio.Run.Views;

/// <summary>
/// The mount form of SPEC §4.5, in its launcher incarnation: physical path, virtual path,
/// a closed rights list, and the optional per-sub-path overrides. Nothing here spells the
/// Docker-style mount string by hand — <see cref="MountDefinition"/> serializes it and the
/// domain parser is what accepts or rejects the result.
/// </summary>
internal sealed class MountFormDialog : Window
{
    private readonly TextField _physical;
    private readonly TextField _virtual;
    private readonly ListView _rights;
    private readonly TextView _overrides;
    private readonly Label _error;

    internal MountFormDialog(MountDefinition? existing)
    {
        Title = existing is null ? "Add a launch mount" : "Edit launch mount";
        X = Pos.Center();
        Y = Pos.Center();
        Width = Dim.Percent(90);
        Height = 18;

        var physicalLabel = new Label { X = 1, Y = 0, Text = "Physical folder :" };
        _physical = new TextField
        {
            X = 19,
            Y = 0,
            Width = Dim.Fill(1),
            Text = existing?.PhysicalPath ?? string.Empty,
        };

        var virtualLabel = new Label { X = 1, Y = 1, Text = "Virtual path    :" };
        _virtual = new TextField
        {
            X = 19,
            Y = 1,
            Width = Dim.Fill(1),
            Text = existing?.VirtualPath ?? MountDefinition.SuggestedVirtualPaths[0],
        };

        var suggestions = new Label
        {
            X = 19,
            Y = 2,
            Width = Dim.Fill(1),
            Text = "Suggestions: " + string.Join(", ", MountDefinition.SuggestedVirtualPaths),
        };

        var rightsLabel = new Label { X = 1, Y = 3, Text = "Rights          :" };
        _rights = new ListView { X = 19, Y = 3, Width = Dim.Fill(1), Height = MountRightsTokens.Choices.Count };
        _rights.SetSource(new ObservableCollection<string>(
            MountRightsTokens.Choices.Select(choice => $"{choice.Token} — {choice.Label}")));
        _rights.SelectedItem = IndexOfRights(existing?.Rights ?? MountRights.ReadOnly);

        var overridesLabel = new Label
        {
            X = 1,
            Y = 3 + MountRightsTokens.Choices.Count,
            Width = Dim.Fill(1),
            Text = "Sub-path overrides, one 'relative/path:rights' per line (optional):",
        };
        _overrides = new TextView
        {
            X = 1,
            Y = 4 + MountRightsTokens.Choices.Count,
            Width = Dim.Fill(1),
            Height = 3,
            Multiline = true,
            Text = FormatOverrides(existing),
        };

        _error = new Label { X = 1, Y = Pos.AnchorEnd(2), Width = Dim.Fill(1), Text = string.Empty };

        var accept = new Button { X = 1, Y = Pos.AnchorEnd(1), Text = "OK", IsDefault = true };
        var cancel = new Button { X = Pos.Right(accept) + 2, Y = Pos.AnchorEnd(1), Text = "Cancel" };

        accept.Accepting += (_, _) => TryAccept();
        cancel.Accepting += (_, _) =>
        {
            Mount = null;
            TerminalApp.RequestStop(this);
        };

        Add(physicalLabel, _physical, virtualLabel, _virtual, suggestions,
            rightsLabel, _rights, overridesLabel, _overrides, _error, accept, cancel);
    }

    /// <summary>The mount the user described, or null when the form was cancelled.</summary>
    internal MountDefinition? Mount { get; private set; }

    /// <summary>Runs the form modally; returns the mount, or null on cancel.</summary>
    internal static MountDefinition? Show(MountDefinition? existing = null)
    {
        using var dialog = new MountFormDialog(existing);
        TerminalApp.Run(dialog, errorHandler: null);
        return dialog.Mount;
    }

    private void TryAccept()
    {
        var rightsIndex = Math.Clamp(_rights.SelectedItem ?? 0, 0, MountRightsTokens.Choices.Count - 1);

        var candidate = new MountDefinition
        {
            PhysicalPath = (_physical.Text ?? string.Empty).Trim(),
            VirtualPath = (_virtual.Text ?? string.Empty).Trim(),
            Rights = MountRightsTokens.Choices[rightsIndex].Rights,
            Overrides = ParseOverrides(_overrides.Text, out var overrideError),
        };

        if (overrideError is not null)
        {
            _error.Text = overrideError;
            return;
        }

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

    private static int IndexOfRights(MountRights rights)
    {
        for (var i = 0; i < MountRightsTokens.Choices.Count; i++)
        {
            if (MountRightsTokens.Choices[i].Rights == rights)
                return i;
        }

        return 0;
    }

    private static string FormatOverrides(MountDefinition? mount) =>
        mount is null
            ? string.Empty
            : string.Join(
                Environment.NewLine,
                mount.Overrides.Select(item => $"{item.RelativePath}:{MountRightsTokens.ToToken(item.Rights)}"));

    private static List<SubPathRightsOverride> ParseOverrides(string? text, out string? error)
    {
        error = null;
        var parsed = new List<SubPathRightsOverride>();

        foreach (var line in (text ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            var separator = trimmed.LastIndexOf(':');
            if (separator <= 0)
            {
                error = $"'{trimmed}' is not a 'relative/path:rights' pair.";
                return [];
            }

            if (!MountRightsTokens.TryParse(trimmed[(separator + 1)..], out var rights))
            {
                error = $"'{trimmed[(separator + 1)..]}' is not one of {string.Join(", ", MountRightsTokens.Tokens)}.";
                return [];
            }

            parsed.Add(new SubPathRightsOverride(trimmed[..separator].Trim(), rights));
        }

        return parsed;
    }
    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Terminal.Gui's base View.Dispose also disposes the Add()-ed subviews; their
            // IsDisposed guard makes these explicit calls idempotent no-ops, and satisfies the
            // "owned IDisposable field" rule the repo builds with.
            _physical.Dispose();
            _virtual.Dispose();
            _rights.Dispose();
            _overrides.Dispose();
            _error.Dispose();
        }

        base.Dispose(disposing);
    }

}
