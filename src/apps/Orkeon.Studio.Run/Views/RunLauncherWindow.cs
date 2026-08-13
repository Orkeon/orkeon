using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Core.Validation;
using Orkeon.Studio.Run.Launcher;
using Terminal.Gui.App;
using TerminalApp = Terminal.Gui.App.Application;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Orkeon.Studio.Run.Views;

/// <summary>
/// The launcher screen. It renders <see cref="RunLauncherViewModel"/> and decides nothing:
/// which shapes exist, which options apply to them, what the command line is and what an
/// exit code means all come from <c>Orkeon.Studio.Core</c>.
/// </summary>
internal sealed class RunLauncherWindow : Window
{
    private readonly RunLauncherViewModel _launcher;

    private readonly TextField _targetField;
    private readonly Label _targetStatus;
    private readonly CheckBox _automaticSettings;
    private readonly TextField _settingsField;

    private readonly FrameView _optionsFrame;
    private readonly Label _variablesLabel;
    private readonly TextView _variablesField;
    private readonly Label _initialContextLabel;
    private readonly TextField _initialContextField;
    private readonly Label _inputsLabel;
    private readonly TextField _inputsField;
    private readonly Label _inputsFileLabel;
    private readonly TextField _inputsFileField;
    private readonly TextField _verbosityField;
    private readonly CheckBox _llmLog;
    private readonly TextField _llmLogPathField;

    private readonly Label _mountsSummary;
    private readonly TextField _commandLine;
    private readonly Button _validateButton;
    private readonly Button _runButton;
    private readonly Button _stopButton;
    private readonly Label _statusLabel;
    private readonly LogsPaneView _logs;

    internal RunLauncherWindow(RunLauncherViewModel launcher)
    {
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));

        Title = $"{Cli.StudioRunInfo.ToolName} — {Cli.StudioRunInfo.Version}   (Esc: stop run · Ctrl+Q: quit)";
        BorderStyle = Terminal.Gui.Drawing.LineStyle.None;

        // ── Target ────────────────────────────────────────────────────────────────
        var targetLabel = new Label { X = 0, Y = 0, Text = "Crew:" };
        _targetField = new TextField { X = 6, Y = 0, Width = Dim.Fill(24) };
        var detectButton = new Button { X = Pos.Right(_targetField) + 1, Y = 0, Text = "Detect" };
        var browseButton = new Button { X = Pos.Right(detectButton) + 1, Y = 0, Text = "Choose…" };
        _targetStatus = new Label { X = 0, Y = 1, Width = Dim.Fill(), Height = 2, Text = "No target selected." };

        // ── Settings ──────────────────────────────────────────────────────────────
        _automaticSettings = new CheckBox
        {
            X = 0,
            Y = 3,
            Text = "Settings: auto",
            Value = CheckState.Checked,
        };
        _settingsField = new TextField
        {
            X = Pos.Right(_automaticSettings) + 2,
            Y = 3,
            Width = Dim.Fill(12),
            Enabled = false,
        };
        var chainButton = new Button { X = Pos.Right(_settingsField) + 1, Y = 3, Text = "Chain…" };

        // ── Options ───────────────────────────────────────────────────────────────
        _optionsFrame = new FrameView { X = 0, Y = 4, Width = Dim.Fill(), Height = 9, Title = "Options" };

        _variablesLabel = new Label { X = 0, Y = 0, Text = "-V KEY=VALUE (one per line):" };
        _variablesField = new TextView { X = 30, Y = 0, Width = Dim.Fill(), Height = 2, Multiline = true };
        _initialContextLabel = new Label { X = 0, Y = 2, Text = "--initial-context:" };
        _initialContextField = new TextField { X = 30, Y = 2, Width = Dim.Fill() };
        _inputsLabel = new Label { X = 0, Y = 3, Text = "--inputs (JSON):" };
        _inputsField = new TextField { X = 30, Y = 3, Width = Dim.Fill() };
        _inputsFileLabel = new Label { X = 0, Y = 4, Text = "--inputs-file:" };
        _inputsFileField = new TextField { X = 30, Y = 4, Width = Dim.Fill() };

        var verbosityLabel = new Label { X = 0, Y = 5, Text = "--verbose (0|1|2):" };
        _verbosityField = new TextField { X = 30, Y = 5, Width = 3, Text = "0" };
        _llmLog = new CheckBox { X = 36, Y = 5, Text = "--llm-log" };
        var llmLogPathLabel = new Label { X = 50, Y = 5, Text = "path:" };
        _llmLogPathField = new TextField { X = 56, Y = 5, Width = Dim.Fill() };

        _optionsFrame.Add(
            _variablesLabel, _variablesField,
            _initialContextLabel, _initialContextField,
            _inputsLabel, _inputsField,
            _inputsFileLabel, _inputsFileField,
            verbosityLabel, _verbosityField, _llmLog, llmLogPathLabel, _llmLogPathField);

        // ── Mounts, command line, actions ─────────────────────────────────────────
        var mountsButton = new Button { X = 0, Y = 13, Text = "Mounts…" };
        _mountsSummary = new Label { X = Pos.Right(mountsButton) + 2, Y = 13, Width = Dim.Fill() };

        var commandLabel = new Label { X = 0, Y = 14, Text = "Command:" };
        _commandLine = new TextField
        {
            X = 9,
            Y = 14,
            Width = Dim.Fill(),
            ReadOnly = true,
            Text = _launcher.DescribeCommandLine(),
        };

        _validateButton = new Button { X = 0, Y = 15, Text = "Validate (dry-run)" };
        _runButton = new Button { X = Pos.Right(_validateButton) + 1, Y = 15, Text = "Run", IsDefault = true };
        _stopButton = new Button { X = Pos.Right(_runButton) + 1, Y = 15, Text = "Stop", Enabled = false };
        var historyButton = new Button { X = Pos.Right(_stopButton) + 1, Y = 15, Text = "History…" };
        var quitButton = new Button { X = Pos.Right(historyButton) + 1, Y = 15, Text = "Quit" };

        _statusLabel = new Label { X = 0, Y = 16, Width = Dim.Fill(), Text = "Ready." };

        _logs = new LogsPaneView(new TerminalGuiOptions { LogsPaneTitle = "Output" })
        {
            X = 0,
            Y = 17,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };

        Add(targetLabel, _targetField, detectButton, browseButton, _targetStatus,
            _automaticSettings, _settingsField, chainButton,
            _optionsFrame,
            mountsButton, _mountsSummary,
            commandLabel, _commandLine,
            _validateButton, _runButton, _stopButton, historyButton, quitButton,
            _statusLabel, _logs);

        // ── Wiring ────────────────────────────────────────────────────────────────
        detectButton.Accepting += (_, _) => DetectTarget();
        _targetField.Accepting += (_, _) => DetectTarget();
        browseButton.Accepting += (_, _) => ChooseCandidate();
        chainButton.Accepting += (_, _) =>
            InfoDialog.Show("Settings resolution chain", LaunchOptionsModel.DescribeResolutionChain());

        _automaticSettings.ValueChanged += (_, _) =>
        {
            _settingsField.Enabled = _automaticSettings.Value != CheckState.Checked;
            CollectForm();
            RefreshCommandLine();
        };

        mountsButton.Accepting += (_, _) =>
        {
            MountsDialog.Show(_launcher);
            RefreshMountsSummary();
            RefreshCommandLine();
        };

        _validateButton.Accepting += (_, _) => StartRun(dryRun: true);
        _runButton.Accepting += (_, _) => StartRun(dryRun: false);
        _stopButton.Accepting += (_, _) => RequestStopOfRun();
        historyButton.Accepting += (_, _) => ShowHistory();
        quitButton.Accepting += (_, _) => Quit();

        // Two subscriptions on purpose. The view-level one is what a focused button sees;
        // the application-level one fires before any view handler, which is the only thing
        // that reaches Escape while a text field has focus (the same swallowing the CLI
        // console hit with Ctrl+C).
        KeyDown += OnKeyDown;
        TerminalApp.KeyDown += OnKeyDown;

        RefreshTargetStatus();
        RefreshOptionAvailability();
        RefreshMountsSummary();
        ReportBinaryLocation();
        LoadHistoryInBackground();
    }

    /// <summary>Process exit code once the window closes.</summary>
    internal int ExitCode { get; private set; }

    private void OnKeyDown(object? sender, Key key)
    {
        if (key.KeyCode == KeyCode.Esc && _launcher.Session.IsRunning)
        {
            key.Handled = true;
            RequestStopOfRun();
            return;
        }

        var stripped = key.KeyCode & ~(KeyCode.CtrlMask | KeyCode.AltMask | KeyCode.ShiftMask);
        if (key.IsCtrl && stripped == KeyCode.Q)
        {
            key.Handled = true;
            Quit();
        }
    }

    // ── Target ────────────────────────────────────────────────────────────────────

    private void DetectTarget()
    {
        _launcher.Target.Select(_targetField.Text);

        if (_launcher.Target.NeedsShapeChoice)
            AskWhichShape();

        RefreshTargetStatus();
        RefreshOptionAvailability();
        RefreshCommandLine();
    }

    private void AskWhichShape()
    {
        // Both shapes are offered by name; the detector applies no precedence and neither do we.
        var choice = ChoiceDialog.Show(
            "Two run shapes at once",
            _launcher.Target.Error ?? string.Empty,
            [
                TargetSelectionModel.DescribeKind(RunTargetKind.MultiFileCrewDirectory),
                TargetSelectionModel.DescribeKind(RunTargetKind.ScriptDirectory),
            ],
            "Run this one");

        if (choice < 0)
            return;

        _launcher.Target.ResolveShape(
            choice == 0 ? RunTargetKind.MultiFileCrewDirectory : RunTargetKind.ScriptDirectory);
    }

    private void ChooseCandidate()
    {
        if (_launcher.Target.State != TargetSelectionState.NeedsSelection)
        {
            DetectTarget();
            if (_launcher.Target.State != TargetSelectionState.NeedsSelection)
                return;
        }

        var candidates = _launcher.Target.Candidates;
        var choice = ChoiceDialog.Show(
            "Which script?",
            $"'{_launcher.Target.SelectedPath}' holds no {RunTargetDetector.CrewScriptFileName}. Pick the script to run:",
            candidates,
            "Run this one");

        if (choice < 0 || choice >= candidates.Count)
            return;

        _launcher.Target.SelectCandidate(candidates[choice]);
        _targetField.Text = _launcher.Target.SelectedPath ?? string.Empty;

        RefreshTargetStatus();
        RefreshOptionAvailability();
        RefreshCommandLine();
    }

    private void RefreshTargetStatus()
    {
        var target = _launcher.Target;

        _targetStatus.Text = target.State switch
        {
            TargetSelectionState.Resolved => target.FrameworkRequirement is { } requirement
                ? $"{target.ShapeDescription} — runs: {target.Target!.RunPath}{Environment.NewLine}{requirement}"
                : $"{target.ShapeDescription} — runs: {target.Target!.RunPath}",
            TargetSelectionState.NeedsSelection =>
                $"{target.Candidates.Count} script(s) found — press 'Choose…' to pick one.",
            TargetSelectionState.Failed => target.Error ?? "This path holds no crew definition.",
            _ => "No target selected.",
        };
    }

    // ── Options ───────────────────────────────────────────────────────────────────

    private void RefreshOptionAvailability()
    {
        // The two dialects' fields stay on screen, disabled: the user sees which options the
        // detected shape accepts instead of watching the form change size under them.
        SetOptionEnabled(_variablesLabel, _variablesField, RunOption.Variables, "-V KEY=VALUE (one per line):");
        SetOptionEnabled(_initialContextLabel, _initialContextField, RunOption.InitialContext, "--initial-context:");
        SetOptionEnabled(_inputsLabel, _inputsField, RunOption.Inputs, "--inputs (JSON):");
        SetOptionEnabled(_inputsFileLabel, _inputsFileField, RunOption.InputsFile, "--inputs-file:");

        _optionsFrame.Title = _launcher.Target.Target is { } target
            ? $"Options — {target.Dialect} target"
            : "Options";
    }

    private void SetOptionEnabled(Label label, View field, RunOption option, string caption)
    {
        var available = _launcher.IsOptionAvailable(option);
        field.Enabled = available;
        field.Visible = available || _launcher.Target.Target is null;
        label.Visible = field.Visible;
        label.Text = available ? caption : caption + " (n/a)";
    }

    /// <summary>Reads the on-screen fields back into the model before anything uses them.</summary>
    private void CollectForm()
    {
        var options = _launcher.Options;

        options.UseAutomaticSettings = _automaticSettings.Value == CheckState.Checked;
        options.ExplicitSettingsPath = _settingsField.Text;
        options.SetVariablesFromText(_variablesField.Text);
        options.InitialContext = NullIfBlank(_initialContextField.Text);
        options.InputsJson = NullIfBlank(_inputsField.Text);
        options.InputsFilePath = NullIfBlank(_inputsFileField.Text);
        options.LlmLogEnabled = _llmLog.Value == CheckState.Checked;
        options.LlmLogPath = NullIfBlank(_llmLogPathField.Text);
        options.Verbosity = int.TryParse(_verbosityField.Text, CultureInfo.InvariantCulture, out var verbosity)
            ? verbosity
            : 0;

        _launcher.RefreshSettingsMounts();
    }

    private void RefreshCommandLine()
    {
        CollectForm();
        _commandLine.Text = _launcher.DescribeCommandLine();
    }

    private void RefreshMountsSummary()
    {
        var count = _launcher.Options.Mounts.Count;
        var external = _launcher.Options.AllowExternalMounts ? " · --allow-external-mounts" : string.Empty;
        _mountsSummary.Text = count == 0
            ? "No launch mount — the appsettings mounts are used as-is."
            : string.Create(CultureInfo.InvariantCulture, $"{count} launch mount(s) override index 0..{count - 1}{external}");
    }

    // ── Running ───────────────────────────────────────────────────────────────────

    [SuppressMessage("Design", "CA1031",
        Justification = "The run happens on a background task; any failure of the child process must " +
                        "become a status line and a log entry, never an unobserved exception that takes " +
                        "the UI's event loop down with it.")]
    private void StartRun(bool dryRun)
    {
        if (_launcher.Session.IsRunning)
            return;

        if (!_launcher.Target.IsResolved)
        {
            _statusLabel.Text = "Select a crew first.";
            return;
        }

        CollectForm();

        var errors = _launcher.Validate(dryRun)
            .Where(message => message.Severity == ValidationSeverity.Error)
            .ToList();

        if (errors.Count > 0)
        {
            InfoDialog.Show("These options cannot be launched", errors.Select(Describe));
            return;
        }

        foreach (var warning in _launcher.Validate(dryRun).Where(m => m.Severity == ValidationSeverity.Warning))
            _logs.Append("! " + Describe(warning));

        _logs.Append("$ " + _launcher.DescribeCommandLine(dryRun));
        SetRunning(true, dryRun ? "Validating…" : "Running…");

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _launcher.LaunchAsync(dryRun, AppendOutput).ConfigureAwait(false);
                TerminalApp.Invoke(() => Finish(LaunchOutcomeFormatter.Describe(result, dryRun)));
            }
            catch (Exception ex)
            {
                _logs.Append("! " + ex.Message);
                TerminalApp.Invoke(() => Finish("The launch failed before the process started."));
            }
        });
    }

    [SuppressMessage("Design", "CA1031",
        Justification = "Same as StartRun: a replay runs on a background task and its failures belong " +
                        "in the log pane, not in an unobserved task exception.")]
    private void StartReplay(Orkeon.Studio.Core.History.LaunchHistoryEntry entry)
    {
        if (_launcher.Session.IsRunning)
            return;

        _launcher.ApplyHistoryEntry(entry);
        _targetField.Text = entry.Target;
        _automaticSettings.Value = string.IsNullOrWhiteSpace(entry.SettingsPath) ? CheckState.Checked : CheckState.UnChecked;
        _settingsField.Text = entry.SettingsPath ?? string.Empty;
        RefreshTargetStatus();
        RefreshOptionAvailability();

        _logs.Append("$ " + Orkeon.Studio.Core.Launch.CommandLineDisplay.Format(entry.Arguments));
        SetRunning(true, "Replaying…");

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _launcher.ReplayAsync(entry, AppendOutput).ConfigureAwait(false);
                TerminalApp.Invoke(() => Finish(LaunchOutcomeFormatter.DescribeRun(result)));
            }
            catch (Exception ex)
            {
                _logs.Append("! " + ex.Message);
                TerminalApp.Invoke(() => Finish("The replay failed before the process started."));
            }
        });
    }

    private void AppendOutput(ProcessOutputLine line) =>
        _logs.Append(line.Channel == ProcessOutputChannel.StandardError ? "err| " + line.Text : line.Text);

    private void RequestStopOfRun()
    {
        if (_launcher.Session.RequestCancellation())
            _statusLabel.Text = "Stop requested — asking the process to shut down (SIGINT), then killing it.";
    }

    private void SetRunning(bool running, string status)
    {
        _statusLabel.Text = status;
        _runButton.Enabled = !running;
        _validateButton.Enabled = !running;
        _stopButton.Enabled = running;
    }

    private void Finish(string status)
    {
        SetRunning(false, status);
        _commandLine.Text = _launcher.DescribeCommandLine();
    }

    // ── History ───────────────────────────────────────────────────────────────────

    private void ShowHistory()
    {
        var entries = _launcher.Session.History.Entries;

        if (entries.Count == 0)
        {
            InfoDialog.Show("Recent launches", "No launch has been recorded yet.");
            return;
        }

        var choice = ChoiceDialog.Show(
            "Recent launches",
            "Pick a launch to run again with exactly the arguments it used:",
            _launcher.DescribeHistory(),
            "Run again");

        if (choice >= 0 && choice < entries.Count)
            StartReplay(entries[choice]);
    }

    private void LoadHistoryInBackground() =>
        _ = Task.Run(async () => await _launcher.Session.LoadHistoryAsync().ConfigureAwait(false));

    // ── Misc ──────────────────────────────────────────────────────────────────────

    private void ReportBinaryLocation()
    {
        var location = _launcher.Session.LocateBinary();
        _statusLabel.Text = location.Found
            ? $"Ready — using {location.Path}"
            : location.Error ?? "The `orkeon` command-line tool was not found.";
    }

    private void Quit()
    {
        _launcher.Session.RequestCancellation();
        ExitCode = 0;
        TerminalApp.RequestStop(this);
    }

    // ── Test seams ────────────────────────────────────────────────────────────────
    // Terminal.Gui offers no headless driver here, so the screen is exercised the way the
    // rest of the repo exercises its views: build it, drive it through these accessors, read
    // the state back. Nothing below is called by the running application.

    /// <summary>Test-only: does what the Detect button does for <paramref name="path"/>.</summary>
    internal void SelectTargetForTest(string path)
    {
        _targetField.Text = path;
        DetectTarget();
    }

    /// <summary>Test-only: whether the field of <paramref name="option"/> is usable on screen.</summary>
    internal bool IsOptionFieldEnabled(RunOption option) => option switch
    {
        RunOption.Variables => _variablesField.Enabled,
        RunOption.InitialContext => _initialContextField.Enabled,
        RunOption.Inputs => _inputsField.Enabled,
        RunOption.InputsFile => _inputsFileField.Enabled,
        RunOption.Verbose => _verbosityField.Enabled,
        RunOption.LlmLog => _llmLog.Enabled,
        RunOption.Settings => _settingsField.Enabled,
        _ => true,
    };

    /// <summary>Test-only: the line under the target field.</summary>
    internal string TargetStatusText => _targetStatus.Text;

    /// <summary>Test-only: the command-line preview field.</summary>
    internal string CommandLineText => _commandLine.Text;

    /// <summary>Test-only: the status line.</summary>
    internal string StatusText => _statusLabel.Text;

    /// <summary>
    /// One finding, rendered by the shared Core formatter so its severity is spelled out here
    /// exactly as it is in the appsettings editor and in the WPF lists.
    /// </summary>
    private static string Describe(ValidationMessage message) => ValidationMessageFormatter.Format(message);

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            TerminalApp.KeyDown -= OnKeyDown;

            // Terminal.Gui's base View.Dispose also disposes the Add()-ed subviews; their
            // IsDisposed guard makes these explicit calls idempotent no-ops, and satisfies the
            // "owned IDisposable field" rule the repo builds with.
            _targetField.Dispose();
            _targetStatus.Dispose();
            _automaticSettings.Dispose();
            _settingsField.Dispose();
            _optionsFrame.Dispose();
            _variablesLabel.Dispose();
            _variablesField.Dispose();
            _initialContextLabel.Dispose();
            _initialContextField.Dispose();
            _inputsLabel.Dispose();
            _inputsField.Dispose();
            _inputsFileLabel.Dispose();
            _inputsFileField.Dispose();
            _verbosityField.Dispose();
            _llmLog.Dispose();
            _llmLogPathField.Dispose();
            _mountsSummary.Dispose();
            _commandLine.Dispose();
            _validateButton.Dispose();
            _runButton.Dispose();
            _stopButton.Dispose();
            _statusLabel.Dispose();
            _logs.Dispose();
        }

        base.Dispose(disposing);
    }

}
