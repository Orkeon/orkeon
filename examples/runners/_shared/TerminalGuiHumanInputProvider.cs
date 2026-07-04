using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Constants.HumanInput;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.HumanInput;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;
using TgApp = Terminal.Gui.App.Application;

namespace Orkeon.Examples.Shared;

/// <summary>
/// <see cref="IHumanInputProvider"/> implementation that displays modal
/// Terminal.Gui dialogs to collect user responses. Designed to be used
/// from the split-pane console — the dialog appears centred over the TUI.
/// </summary>
public sealed partial class TerminalGuiHumanInputProvider : IHumanInputProvider
{
    private readonly ILogger<TerminalGuiHumanInputProvider> _logger;
    private readonly IFileSystemService? _fileSystem;

    /// <summary>Initializes a new instance of <see cref="TerminalGuiHumanInputProvider"/>.</summary>
    /// <param name="logger">Optional logger.</param>
    /// <param name="fileSystem">
    /// Optional VFS. When present and the caller sets
    /// <see cref="HumanInputDefaults.EditFilePathMetadataKey"/> on
    /// <see cref="HumanInputContext.Metadata"/>, choices whose value contains
    /// the substring <c>edit</c> (case-insensitive) open an inline editor on
    /// that file. Without VFS, such choices are returned as plain tokens.
    /// </param>
    public TerminalGuiHumanInputProvider(
        ILogger<TerminalGuiHumanInputProvider>? logger = null,
        IFileSystemService? fileSystem = null)
    {
        _logger = logger ?? NullLogger<TerminalGuiHumanInputProvider>.Instance;
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Returns the editable file path declared by the caller via
    /// <see cref="HumanInputDefaults.EditFilePathMetadataKey"/>, or <c>null</c>.
    /// </summary>
    private static string? GetEditFilePathFromMetadata(HumanInputContext context)
    {
        if (context.Metadata is null) return null;
        if (!context.Metadata.TryGetValue(HumanInputDefaults.EditFilePathMetadataKey, out var raw))
            return null;
        var s = raw as string;
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    /// <summary>
    /// Convention: a choice opens the inline editor when its value contains
    /// the substring <c>edit</c> (case-insensitive). Documented on the
    /// <c>edit_file_path</c> field of the <c>human_input</c> tool.
    /// </summary>
    private static bool IsEditTriggerChoice(string chosen)
        => !string.IsNullOrEmpty(chosen)
           && chosen.Contains("edit", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task<string> GetInputAsync(HumanInputContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => tcs.TrySetCanceled());

        TgApp.Invoke(() =>
        {
            try
            {
                var dlg = new TextInputDialog(context.Prompt, context.DefaultValue ?? string.Empty);
                TgApp.Run(dlg);
                var result = dlg.Cancelled
                    ? (context.DefaultValue ?? string.Empty)
                    : dlg.Value;
                LogTextInputResolved(result.Length, dlg.Cancelled);
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        return tcs.Task;
    }

    /// <inheritdoc />
    public Task<bool> GetConfirmationAsync(HumanInputContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => tcs.TrySetCanceled());

        TgApp.Invoke(() =>
        {
            try
            {
                var dlg = new ConfirmDialog("Human approval required", context.Prompt);
                TgApp.Run(dlg);
                var approved = dlg.Approved;
                LogConfirmationResolved(approved);
                tcs.TrySetResult(approved);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        return tcs.Task;
    }

    /// <inheritdoc />
    public Task<string> GetChoiceAsync(HumanInputContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var options = (context.Options is { Count: > 0 } o ? o.ToArray() : Array.Empty<string>());
        if (options.Length == 0)
        {
            return GetInputAsync(context, cancellationToken);
        }

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => tcs.TrySetCanceled());

        TgApp.Invoke(() =>
        {
            try
            {
                // Loop: cancelling the inline editor re-opens the choice dialog
                // so the user can pick another option (approved / rejected).
                while (true)
                {
                    var dlg = new ChoiceDialog("Choose an option", context.Prompt, options);
                    TgApp.Run(dlg);
                    var idx = dlg.SelectedIndex;
                    var chosen = (idx >= 0 && idx < options.Length)
                        ? options[idx]
                        : (context.DefaultValue ?? options[0]);

                    var editPath = GetEditFilePathFromMetadata(context);
                    var isEditChoice = IsEditTriggerChoice(chosen);
                    LogChoiceDecision(chosen, isEditChoice, editPath ?? "<null>", _fileSystem is not null);

                    // Surface the most common silent-skip case loudly: user picked
                    // "edit_*" but the caller didn't pass edit_file_path in the
                    // tool args (so metadata is empty). Without this, the choice
                    // dialog closes and the pipeline silently treats it as plain
                    // approval — exactly the symptom we're hunting.
                    if (isEditChoice && editPath is null)
                    {
                        ShowEditDiagnostic(
                            "Inline edit unavailable",
                            $"You selected '{chosen}', which is an edit trigger, but the "
                            + "caller did not provide an 'edit_file_path' tool argument "
                            + "(HumanInputContext.Metadata is missing the "
                            + $"'{HumanInputDefaults.EditFilePathMetadataKey}' key).\n\n"
                            + "Falling back to plain choice token. The pipeline will "
                            + "treat the answer as if no edit happened.");
                        LogEditDialogUnavailable("metadata_edit_file_path_missing");
                    }

                    if (isEditChoice && editPath is not null)
                    {
                        // Diagnose failure modes loudly — silent fall-through would
                        // make the user think the edit choice = approved directly.
                        if (_fileSystem is null)
                        {
                            ShowEditDiagnostic(
                                "Inline edit unavailable",
                                "IFileSystemService is not registered for this runner.\n"
                                + "Falling back to plain choice token.");
                            LogEditDialogUnavailable("vfs_not_registered");
                        }
                        else
                        {
                            // Block on the TG event-loop thread is acceptable here:
                            // we are already inside a nested Run-loop owned by Terminal.Gui.
                            string? content = null;
                            string? readError = null;
                            try
                            {
                                content = _fileSystem
                                    .TryReadAllTextAsync(editPath, cancellationToken)
                                    .GetAwaiter().GetResult();
                            }
                            catch (Exception readEx)
                            {
                                readError = readEx.Message;
                                LogEditDialogReadFailed(editPath, readEx.Message);
                            }

                            if (content is null)
                            {
                                ShowEditDiagnostic(
                                    "Inline edit unavailable",
                                    $"Could not read file: {editPath}\n"
                                    + (readError is null
                                        ? "VFS returned null (file does not exist or no Read right)."
                                        : $"VFS threw: {readError}"));
                            }
                            else
                            {
                                var editDlg = new EditDialog(editPath, content);
                                TgApp.Run(editDlg);
                                if (editDlg.Cancelled)
                                {
                                    // Re-show the ChoiceDialog so the user picks again.
                                    continue;
                                }
                                try
                                {
                                    _fileSystem
                                        .WriteAllTextAsync(editPath, editDlg.Value, cancellationToken)
                                        .GetAwaiter().GetResult();
                                    LogEditDialogSaved(editPath, editDlg.Value.Length);
                                }
                                catch (Exception writeEx)
                                {
                                    LogEditDialogWriteFailed(editPath, writeEx.Message);
                                    ShowEditDiagnostic(
                                        "Edit saved — write failed",
                                        $"Could not write back to: {editPath}\nVFS threw: {writeEx.Message}\n\n"
                                        + "Your edits were not persisted. Approval will proceed anyway.");
                                }
                            }
                        }
                    }

                    LogChoiceResolved(chosen.Length);
                    tcs.TrySetResult(chosen);
                    return;
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        return tcs.Task;
    }

    /// <inheritdoc />
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(TgApp.Initialized);

    [LoggerMessage(Level = LogLevel.Information, Message = "Terminal.Gui text input resolved (len={Length}, cancelled={Cancelled})")]
    private partial void LogTextInputResolved(int length, bool cancelled);

    [LoggerMessage(Level = LogLevel.Information, Message = "Terminal.Gui confirmation resolved (approved={Approved})")]
    private partial void LogConfirmationResolved(bool approved);

    [LoggerMessage(Level = LogLevel.Information, Message = "Terminal.Gui choice resolved (chosen_len={Length})")]
    private partial void LogChoiceResolved(int length);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Terminal.Gui edit dialog could not read {Path}: {Error}")]
    private partial void LogEditDialogReadFailed(string path, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Terminal.Gui edit dialog saved {Path} ({Length} chars)")]
    private partial void LogEditDialogSaved(string path, int length);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Terminal.Gui edit dialog could not write {Path}: {Error}")]
    private partial void LogEditDialogWriteFailed(string path, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Terminal.Gui edit dialog unavailable ({Reason})")]
    private partial void LogEditDialogUnavailable(string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Terminal.Gui choice decision: chosen={Chosen}, is_edit_trigger={IsEdit}, edit_file_path={Path}, vfs_present={Vfs}")]
    private partial void LogChoiceDecision(string chosen, bool isEdit, string path, bool vfs);

    private static void ShowEditDiagnostic(string title, string message)
    {
        // The caller-created toplevel is NOT auto-disposed by Application.Run, so
        // dispose it here (which also disposes its child views) once the modal closes.
        using var dlg = new InfoDialog(title, message);
        TgApp.Run(dlg);
    }
}

/// <summary>
/// Builds explicit fg/bg <see cref="Scheme"/> instances for the human-input dialogs.
/// Terminal.Gui 2.x default schemes paint gray-on-gray in many terminals, which makes
/// the message text disappear behind the window background — these helpers force
/// white-on-black for read-only content and white-on-blue (focused) for input fields.
/// </summary>
/// <remarks>
/// This mirrors <c>SchemeFactory</c> in <c>Orkeon.Cli.TerminalGui.Layout</c>; we duplicate
/// it locally to avoid pulling a project reference that would re-introduce the
/// <c>Application</c> namespace collision between <c>Orkeon.Application</c> and
/// <c>Terminal.Gui.App.Application</c>.
/// </remarks>
internal static class DialogSchemes
{
    public static Scheme Pane() => Build(
        fg: ColorName16.White,
        bg: ColorName16.Black,
        focusFg: ColorName16.BrightCyan,
        focusBg: ColorName16.Black,
        hotFg: ColorName16.BrightYellow);

    public static Scheme Editable() => Build(
        fg: ColorName16.White,
        bg: ColorName16.Black,
        focusFg: ColorName16.White,
        focusBg: ColorName16.Blue,
        hotFg: ColorName16.BrightYellow);

    private static Scheme Build(ColorName16 fg, ColorName16 bg, ColorName16 focusFg, ColorName16 focusBg, ColorName16 hotFg)
    {
        var fgC = new Color(fg);
        var bgC = new Color(bg);
        var focusFgC = new Color(focusFg);
        var focusBgC = new Color(focusBg);
        var hotFgC = new Color(hotFg);
        return new Scheme
        {
            Normal    = new Attribute(in fgC,      in bgC),
            Focus     = new Attribute(in focusFgC, in focusBgC),
            HotNormal = new Attribute(in hotFgC,   in bgC),
            HotFocus  = new Attribute(in hotFgC,   in focusBgC),
            Active    = new Attribute(in fgC,      in bgC),
            HotActive = new Attribute(in hotFgC,   in bgC),
            Highlight = new Attribute(in bgC,      in fgC),
            Editable  = new Attribute(in fgC,      in bgC),
            ReadOnly  = new Attribute(in fgC,      in bgC),
            Disabled  = new Attribute(in fgC,      in bgC),
        };
    }
}

/// <summary>
/// Read-only <see cref="TextView"/> that auto-copies the current selection to
/// the OS clipboard on left-button release and renders the selection in
/// inverse video (black-on-white) for clear visual feedback.
/// </summary>
/// <remarks>
/// Mirrors <c>MouseClipboardTextView</c> in <c>Orkeon.Cli.TerminalGui.Layout</c>;
/// duplicated locally so the human-input dialog can offer drag-select + auto-copy
/// without referencing the Cli.TerminalGui project (which would re-introduce the
/// <c>Orkeon.Application</c> ↔ <c>Terminal.Gui.App.Application</c> namespace collision).
/// </remarks>
internal sealed class ClipboardCopyTextView : TextView
{
    private static readonly Color SelectionFg = new(ColorName16.Black);
    private static readonly Color SelectionBg = new(ColorName16.White);
    private static readonly Attribute SelectionAttribute = new(in SelectionFg, in SelectionBg);

    protected override bool OnMouseEvent(Mouse mouseEvent)
    {
        var handled = base.OnMouseEvent(mouseEvent);

        if (mouseEvent.Flags.HasFlag(MouseFlags.LeftButtonReleased)
            || mouseEvent.Flags.HasFlag(MouseFlags.LeftButtonClicked))
        {
            var selected = SelectedText;
            var clipboard = TgApp.Clipboard;
            if (clipboard is not null && !string.IsNullOrEmpty(selected))
            {
                clipboard.TrySetClipboardData(selected);
            }
        }

        return handled;
    }

    protected override void OnDrawSelectionColor(List<Cell> line, int idxCol, int idxRow)
    {
        SetAttribute(SelectionAttribute);
    }
}

/// <summary>
/// Modal dialog with a scrollable read-only message area and a single TextField
/// + OK/Cancel buttons.
/// </summary>
/// <remarks>
/// Sizes are screen-relative (<see cref="Dim.Percent(int, DimPercentMode)"/>) so the
/// dialog never overflows the terminal — long prompts (markdown summaries, etc.)
/// scroll inside the message area instead of pushing the window off-screen.
/// </remarks>
internal sealed class TextInputDialog : Window
{
    public string Value { get; private set; } = string.Empty;
    public bool Cancelled { get; private set; }

    public TextInputDialog(string prompt, string initial)
    {
        Title = "Human input required";
        Width = Dim.Percent(70);
        Height = Dim.Percent(60);
        X = Pos.Center();
        Y = Pos.Center();
        SetScheme(DialogSchemes.Pane());

        // Scrollable read-only TextView so multi-line prompts stay visible.
        // CanFocus=true so drag-select + auto-copy work (mirrors the panes).
        var msgView = new ClipboardCopyTextView
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
            Height = Dim.Fill(5), // reserve rows for the input field + buttons
            Text = prompt,
            ReadOnly = true,
            Multiline = true,
            WordWrap = true,
            CanFocus = true,
            ScrollBars = true,
        };
        msgView.SetScheme(DialogSchemes.Pane());

        var field = new TextField
        {
            X = 1,
            Y = Pos.AnchorEnd(4),
            Width = Dim.Fill(1),
            Text = initial,
        };
        field.SetScheme(DialogSchemes.Editable());

        var ok = new Button
        {
            X = 1,
            Y = Pos.AnchorEnd(2),
            Text = "OK",
            IsDefault = true,
        };
        ok.SetScheme(DialogSchemes.Pane());
        var cancel = new Button
        {
            X = Pos.Right(ok) + 2,
            Y = Pos.AnchorEnd(2),
            Text = "Cancel",
        };
        cancel.SetScheme(DialogSchemes.Pane());

        ok.Accepting += (_, e) =>
        {
            Value = field.Text?.ToString() ?? string.Empty;
            Cancelled = false;
            e.Handled = true; // stop event bubble: prevents the default button's
                              // Accepting from re-firing and overriding our state.
            TgApp.RequestStop(this);
        };
        cancel.Accepting += (_, e) =>
        {
            Value = string.Empty;
            Cancelled = true;
            e.Handled = true;
            TgApp.RequestStop(this);
        };

        Add(msgView, field, ok, cancel);
        Initialized += (_, _) => field.SetFocus();
    }
}

/// <summary>Modal dialog with Yes/No buttons (scrollable message).</summary>
internal sealed class ConfirmDialog : Window
{
    public bool Approved { get; private set; }

    public ConfirmDialog(string title, string message)
    {
        Title = title;
        Width = Dim.Percent(70);
        Height = Dim.Percent(60);
        X = Pos.Center();
        Y = Pos.Center();
        SetScheme(DialogSchemes.Pane());

        var msgView = new ClipboardCopyTextView
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
            Height = Dim.Fill(3),
            Text = message,
            ReadOnly = true,
            Multiline = true,
            WordWrap = true,
            CanFocus = true,
            ScrollBars = true,
        };
        msgView.SetScheme(DialogSchemes.Pane());

        var yes = new Button
        {
            X = 1,
            Y = Pos.AnchorEnd(2),
            Text = "Yes — approve",
            IsDefault = true,
        };
        yes.SetScheme(DialogSchemes.Pane());
        var no = new Button
        {
            X = Pos.Right(yes) + 2,
            Y = Pos.AnchorEnd(2),
            Text = "No — reject",
            IsDefault = false,
        };
        no.SetScheme(DialogSchemes.Pane());

        yes.Accepting += (_, e) => { Approved = true; e.Handled = true; TgApp.RequestStop(this); };
        no.Accepting  += (_, e) => { Approved = false; e.Handled = true; TgApp.RequestStop(this); };

        Add(msgView, yes, no);
    }
}

/// <summary>
/// Modal dialog with N buttons matching the provided choices and a scrollable
/// read-only message area. Sized relative to the screen so it always fits.
/// </summary>
internal sealed class ChoiceDialog : Window
{
    public int SelectedIndex { get; private set; } = -1;

    public ChoiceDialog(string title, string message, string[] choices)
    {
        Title = title;
        Width = Dim.Percent(80);
        Height = Dim.Percent(70);
        X = Pos.Center();
        Y = Pos.Center();
        SetScheme(DialogSchemes.Pane());

        var msgView = new ClipboardCopyTextView
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
            Height = Dim.Fill(3),
            Text = message,
            ReadOnly = true,
            Multiline = true,
            WordWrap = true,
            CanFocus = true,
            ScrollBars = true,
        };
        msgView.SetScheme(DialogSchemes.Pane());

        // Stack buttons horizontally along the bottom. The first button is default.
        // Build every child first, then transfer ownership to this Window in a single
        // Add(...) call: the parent View disposes its children, so the views are owned
        // by 'this' the moment they are added (mirrors the sibling dialogs above).
        var views = new List<View> { msgView };
        Button? previous = null;
        for (int i = 0; i < choices.Length; i++)
        {
            var idx = i;
            var btn = new Button
            {
                X = previous is null ? 1 : Pos.Right(previous) + 2,
                Y = Pos.AnchorEnd(2),
                Text = choices[i],
                IsDefault = i == 0,
            };
            btn.SetScheme(DialogSchemes.Pane());
            btn.Accepting += (_, e) => { SelectedIndex = idx; e.Handled = true; TgApp.RequestStop(this); };
            views.Add(btn);
            previous = btn;
        }

        Add(views.ToArray());
    }
}

/// <summary>
/// Read-only modal with a single OK button — used to surface diagnostic
/// messages (e.g. "could not read file") so failures are visible to the user
/// instead of falling through silently.
/// </summary>
internal sealed class InfoDialog : Window
{
    public InfoDialog(string title, string message)
    {
        Title = title;
        Width = Dim.Percent(70);
        Height = Dim.Percent(60);
        X = Pos.Center();
        Y = Pos.Center();
        SetScheme(DialogSchemes.Pane());

        var msgView = new ClipboardCopyTextView
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
            Height = Dim.Fill(3),
            Text = message,
            ReadOnly = true,
            Multiline = true,
            WordWrap = true,
            CanFocus = true,
            ScrollBars = true,
        };
        msgView.SetScheme(DialogSchemes.Pane());

        var ok = new Button
        {
            X = 1,
            Y = Pos.AnchorEnd(2),
            Text = "OK",
            IsDefault = true,
        };
        ok.SetScheme(DialogSchemes.Pane());
        ok.Accepting += (_, e) => { e.Handled = true; TgApp.RequestStop(this); };

        Add(msgView, ok);
    }
}

/// <summary>
/// Full-screen modal that hosts an editable, multi-line, word-wrapping
/// <see cref="TextView"/> on a file's content. <see cref="Value"/> exposes
/// the final text after the user clicks Approve; <see cref="Cancelled"/>
/// is true when the user dismisses the dialog without saving.
/// </summary>
/// <remarks>
/// Used by <see cref="TerminalGuiHumanInputProvider"/> when the user picks
/// <c>edit_then_approve</c> in the gate choice: the caller reads the file
/// before <c>Run</c>, passes the content here, then writes <see cref="Value"/>
/// back via the VFS once the dialog returns.
/// </remarks>
internal sealed class EditDialog : Window
{
    public string Value { get; private set; } = string.Empty;
    public bool Cancelled { get; private set; }

    public EditDialog(string filePath, string content)
    {
        Title = $"Edit — {filePath}";
        Width = Dim.Percent(90);
        Height = Dim.Percent(85);
        X = Pos.Center();
        Y = Pos.Center();
        SetScheme(DialogSchemes.Pane());

        // NOTE: WordWrap is intentionally OFF. In Terminal.Gui v2.x, enabling
        // WordWrap on a writable TextView blocks character insertion (the view
        // behaves as if ReadOnly). We expose horizontal scrolling instead so
        // edits work as expected.
        //
        // We use ClipboardCopyTextView (same as the panes) so drag-select also
        // pushes to the OS clipboard automatically — no need to press Ctrl+C
        // after selecting. ClipboardCopyTextView is ReadOnly-agnostic; setting
        // ReadOnly=false here keeps the editor fully writable.
        var editor = new ClipboardCopyTextView
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
            Height = Dim.Fill(3),
            Text = content,
            ReadOnly = false,
            Multiline = true,
            WordWrap = false,
            ScrollBars = true,
            CanFocus = true,
        };
        editor.SetScheme(DialogSchemes.Editable());

        var approve = new Button
        {
            X = 1,
            Y = Pos.AnchorEnd(2),
            Text = "Approve & save (Ctrl+Enter)",
            IsDefault = true,
        };
        approve.SetScheme(DialogSchemes.Pane());
        var cancel = new Button
        {
            X = Pos.Right(approve) + 2,
            Y = Pos.AnchorEnd(2),
            Text = "Cancel (back to choices)",
        };
        cancel.SetScheme(DialogSchemes.Pane());

        approve.Accepting += (_, e) =>
        {
            Value = editor.Text?.ToString() ?? string.Empty;
            Cancelled = false;
            e.Handled = true;
            TgApp.RequestStop(this);
        };
        cancel.Accepting += (_, e) =>
        {
            Value = content;
            Cancelled = true;
            e.Handled = true;
            TgApp.RequestStop(this);
        };

        // Ctrl+Enter anywhere in the dialog = approve & save. Strip modifier
        // mask before comparing the base key, matching the pattern used in
        // TerminalGuiHost for the global Ctrl+C handler.
        KeyDown += (_, key) =>
        {
            var stripped = key.KeyCode & ~(Terminal.Gui.Drivers.KeyCode.CtrlMask
                                         | Terminal.Gui.Drivers.KeyCode.AltMask
                                         | Terminal.Gui.Drivers.KeyCode.ShiftMask);
            if (!key.IsCtrl || stripped != Terminal.Gui.Drivers.KeyCode.Enter) return;
            Value = editor.Text?.ToString() ?? string.Empty;
            Cancelled = false;
            key.Handled = true;
            TgApp.RequestStop(this);
        };

        Add(editor, approve, cancel);
        Initialized += (_, _) => editor.SetFocus();
    }
}

/// <summary>DI extension for the Terminal.Gui human-input provider.</summary>
public static class TerminalGuiHumanInputExtensions
{
    /// <summary>
    /// Replaces any registered <see cref="IHumanInputProvider"/> with the
    /// Terminal.Gui modal provider. Must be called AFTER
    /// <c>AddOrkeonHumanInput()</c> so that the <c>human_input</c> tool itself
    /// is also registered.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddTerminalGuiHumanInput(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.RemoveAll<IHumanInputProvider>();
        services.AddSingleton<IHumanInputProvider, TerminalGuiHumanInputProvider>();
        return services;
    }
}
