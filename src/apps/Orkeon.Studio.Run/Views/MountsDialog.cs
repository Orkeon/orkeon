using System.Collections.ObjectModel;
using System.Globalization;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Run.Launcher;
using Terminal.Gui.App;
using TerminalApp = Terminal.Gui.App.Application;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Orkeon.Studio.Run.Views;

/// <summary>
/// The launch's own mounts (SPEC §5.2): the mounts the selected appsettings file already
/// declares, shown read-only, the mounts added for this launch, and — stated rather than
/// implied — the fact that a launch mount <em>replaces</em> the settings mount of the same
/// index instead of joining it.
/// </summary>
internal sealed class MountsDialog : Window
{
    private readonly RunLauncherViewModel _launcher;
    private readonly ListView _mounts;
    private readonly TextView _effective;
    private readonly CheckBox _allowExternal;

    internal MountsDialog(RunLauncherViewModel launcher)
    {
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));

        Title = "Mounts for this launch";
        X = Pos.Center();
        Y = Pos.Center();
        Width = Dim.Percent(95);
        Height = Dim.Percent(90);

        var rule = new Label
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Height = 3,
            Text = LaunchOptionsModel.MountOverrideExplanation,
        };

        var listLabel = new Label { X = 1, Y = 4, Text = "Launch mounts (--mount):" };
        _mounts = new ListView { X = 1, Y = 5, Width = Dim.Fill(1), Height = 5 };

        var add = new Button { X = 1, Y = 10, Text = "Add…" };
        var edit = new Button { X = Pos.Right(add) + 2, Y = 10, Text = "Edit…" };
        var remove = new Button { X = Pos.Right(edit) + 2, Y = 10, Text = "Remove" };

        _allowExternal = new CheckBox
        {
            X = 1,
            Y = 11,
            Text = "--allow-external-mounts (mount roots outside the working directory)",
            Value = _launcher.Options.AllowExternalMounts ? CheckState.Checked : CheckState.UnChecked,
        };

        var securityWarning = new Label
        {
            X = 1,
            Y = 12,
            Width = Dim.Fill(1),
            Height = 3,
            Text = "SECURITY: " + LaunchOptionsModel.ExternalMountsExplanation,
        };

        var effectiveLabel = new Label { X = 1, Y = 15, Text = "Mount list the runtime will see:" };
        _effective = new TextView
        {
            X = 1,
            Y = 16,
            Width = Dim.Fill(1),
            Height = Dim.Fill(2),
            ReadOnly = true,
            Multiline = true,
        };

        var close = new Button { X = 1, Y = Pos.AnchorEnd(1), Text = "Close", IsDefault = true };

        add.Accepting += (_, _) =>
        {
            if (MountFormDialog.Show() is { } mount)
            {
                _launcher.Options.AddMount(mount);
                Refresh();
            }
        };

        edit.Accepting += (_, _) =>
        {
            var index = _mounts.SelectedItem ?? -1;
            if (index < 0 || index >= _launcher.Options.Mounts.Count)
                return;

            if (MountFormDialog.Show(_launcher.Options.Mounts[index]) is { } mount)
            {
                _launcher.Options.ReplaceMountAt(index, mount);
                Refresh();
            }
        };

        remove.Accepting += (_, _) =>
        {
            var index = _mounts.SelectedItem ?? -1;
            if (index < 0 || index >= _launcher.Options.Mounts.Count)
                return;

            _launcher.Options.RemoveMountAt(index);
            Refresh();
        };

        _allowExternal.ValueChanged += (_, _) =>
            _launcher.Options.AllowExternalMounts = _allowExternal.Value == CheckState.Checked;

        close.Accepting += (_, _) => TerminalApp.RequestStop(this);

        Add(rule, listLabel, _mounts, add, edit, remove, _allowExternal, securityWarning,
            effectiveLabel, _effective, close);

        Refresh();
    }

    /// <summary>Runs the panel modally.</summary>
    internal static void Show(RunLauncherViewModel launcher)
    {
        using var dialog = new MountsDialog(launcher);
        TerminalApp.Run(dialog, errorHandler: null);
    }

    private void Refresh()
    {
        var mounts = _launcher.Options.MountStrings;
        _mounts.SetSource(new ObservableCollection<string>(
            mounts.Count == 0 ? ["(none — the appsettings mounts are used as-is)"] : mounts));

        _effective.Text = string.Join(Environment.NewLine, DescribeEffectiveMounts());
    }

    private IEnumerable<string> DescribeEffectiveMounts()
    {
        if (_launcher.SettingsMountsNotice is { } notice)
            yield return notice;

        if (!_launcher.Target.IsResolved)
        {
            yield return RunLauncherViewModel.EffectiveMountsUnknownNotice;
            yield break;
        }

        var effective = _launcher.EffectiveMounts;
        if (effective.Count == 0)
        {
            yield return "No mount is known here.";
            yield break;
        }

        foreach (var mount in effective)
        {
            var origin = DescribeOrigin(mount.Origin);
            var replaced = mount.OverridesSettings
                ? $"  (replaces '{mount.ReplacedSettingsMount}')"
                : string.Empty;

            yield return string.Create(
                CultureInfo.InvariantCulture,
                $"{mount.ConfigurationKey} = {mount.Value}   [{origin}]{replaced}");
        }
    }

    /// <summary>
    /// Names the source of an entry. The auto-injected one is called out rather than folded
    /// into "appsettings": the user never wrote it, and it is what shifts every --mount index.
    /// </summary>
    private static string DescribeOrigin(MountOrigin origin) => origin switch
    {
        MountOrigin.AutoInjected => "auto",
        MountOrigin.CommandLine => "--mount",
        _ => "appsettings",
    };
    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Terminal.Gui's base View.Dispose also disposes the Add()-ed subviews; their
            // IsDisposed guard makes these explicit calls idempotent no-ops, and satisfies the
            // "owned IDisposable field" rule the repo builds with.
            _mounts.Dispose();
            _effective.Dispose();
            _allowExternal.Dispose();
        }

        base.Dispose(disposing);
    }

}
