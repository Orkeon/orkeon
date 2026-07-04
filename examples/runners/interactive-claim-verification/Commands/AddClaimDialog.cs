using Orkeon.Cli.TerminalGui.Layout;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TgApp = Terminal.Gui.App.Application;

namespace Orkeon.Examples.Interactive.ClaimVerification.Commands;

/// <summary>
/// Modal Terminal.Gui dialog used by <c>add-claim</c> to collect all four
/// claim fields (title, verbatim, context, why-selected) in a single screen
/// instead of four sequential console prompts.
/// </summary>
/// <remarks>
/// Sized relatively (<see cref="Dim.Percent(int, DimPercentMode)"/>) so it
/// fits any terminal. Title + verbatim are single-line <see cref="TextField"/>s;
/// context + why-selected are multi-line <see cref="MouseClipboardTextView"/>s
/// (drag-select + auto-copy enabled). Scheme styling matches the rest of the
/// Orkeon TUI (white-on-black panes, blue focus background for editable fields).
/// </remarks>
internal sealed class AddClaimDialog : Window
{
    public string TitleText { get; private set; } = string.Empty;
    public string Verbatim { get; private set; } = string.Empty;
    public string ContextText { get; private set; } = string.Empty;
    public string WhySelected { get; private set; } = string.Empty;
    public bool Cancelled { get; private set; } = true;

    public AddClaimDialog(string suggestedSlot)
    {
        Title = $"New claim (slot {suggestedSlot}) — Tab to navigate, Enter on Create to submit";
        Width = Dim.Percent(80);
        Height = Dim.Percent(80);
        X = Pos.Center();
        Y = Pos.Center();
        SetScheme(SchemeFactory.Pane());

        var titleLabel = new Label { X = 1, Y = 0, Text = "Title:" };
        titleLabel.SetScheme(SchemeFactory.Pane());
        var titleField = new TextField
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
            Height = 1,
        };
        titleField.SetScheme(SchemeFactory.Editable());

        var verbatimLabel = new Label { X = 1, Y = 3, Text = "Claim verbatim (one sentence):" };
        verbatimLabel.SetScheme(SchemeFactory.Pane());
        var verbatimField = new TextField
        {
            X = 1,
            Y = 4,
            Width = Dim.Fill(1),
            Height = 1,
        };
        verbatimField.SetScheme(SchemeFactory.Editable());

        var contextLabel = new Label { X = 1, Y = 6, Text = "Context (optional, multi-line):" };
        contextLabel.SetScheme(SchemeFactory.Pane());
        var contextField = new MouseClipboardTextView
        {
            X = 1,
            Y = 7,
            Width = Dim.Fill(1),
            Height = Dim.Percent(30),
            ReadOnly = false,
            Multiline = true,
            WordWrap = false,
            CanFocus = true,
            ScrollBars = true,
        };
        contextField.SetScheme(SchemeFactory.Editable());

        var whyLabel = new Label
        {
            X = 1,
            Y = Pos.Bottom(contextField) + 1,
            Text = "Why selected (optional, multi-line):",
        };
        whyLabel.SetScheme(SchemeFactory.Pane());
        var whyField = new MouseClipboardTextView
        {
            X = 1,
            Y = Pos.Bottom(whyLabel),
            Width = Dim.Fill(1),
            Height = Dim.Fill(3), // leave 3 rows for the buttons
            ReadOnly = false,
            Multiline = true,
            WordWrap = false,
            CanFocus = true,
            ScrollBars = true,
        };
        whyField.SetScheme(SchemeFactory.Editable());

        var create = new Button
        {
            X = 1,
            Y = Pos.AnchorEnd(2),
            Text = "Create (Ctrl+Enter)",
            IsDefault = true,
        };
        create.SetScheme(SchemeFactory.Pane());
        var cancel = new Button
        {
            X = Pos.Right(create) + 2,
            Y = Pos.AnchorEnd(2),
            Text = "Cancel (Esc)",
        };
        cancel.SetScheme(SchemeFactory.Pane());

        void Submit()
        {
            TitleText = titleField.Text?.ToString() ?? string.Empty;
            Verbatim = verbatimField.Text?.ToString() ?? string.Empty;
            ContextText = contextField.Text?.ToString() ?? string.Empty;
            WhySelected = whyField.Text?.ToString() ?? string.Empty;
            Cancelled = false;
        }

        create.Accepting += (_, e) =>
        {
            Submit();
            e.Handled = true; // stop event bubble so the parent Window doesn't re-fire
            TgApp.RequestStop(this);
        };
        cancel.Accepting += (_, e) =>
        {
            Cancelled = true;
            e.Handled = true;
            TgApp.RequestStop(this);
        };

        // Ctrl+Enter submits from anywhere in the dialog (the multi-line fields
        // consume bare Enter to insert a newline, so we need an explicit shortcut).
        KeyDown += (_, key) =>
        {
            var stripped = key.KeyCode & ~(Terminal.Gui.Drivers.KeyCode.CtrlMask
                                         | Terminal.Gui.Drivers.KeyCode.AltMask
                                         | Terminal.Gui.Drivers.KeyCode.ShiftMask);
            if (key.IsCtrl && stripped == Terminal.Gui.Drivers.KeyCode.Enter)
            {
                Submit();
                key.Handled = true;
                TgApp.RequestStop(this);
                return;
            }
            if (stripped == Terminal.Gui.Drivers.KeyCode.Esc)
            {
                Cancelled = true;
                key.Handled = true;
                TgApp.RequestStop(this);
            }
        };

        Add(titleLabel, titleField,
            verbatimLabel, verbatimField,
            contextLabel, contextField,
            whyLabel, whyField,
            create, cancel);

        Initialized += (_, _) => titleField.SetFocus();
    }
}
