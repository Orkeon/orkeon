using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Runners;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// The panel a tool approval is asked with. Each option's value is the line the approval channel already reads, so
/// the panel answers exactly as a typed reply does.
/// </summary>
internal static class ApprovalPanelRequest
{
    /// <summary>The panel for <paramref name="approval"/>, the first of the <paramref name="waiting"/> questions.</summary>
    public static ConsoleChoiceRequest For(PendingToolApproval approval, int waiting, GlyphSet glyphs)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(glyphs);
        var title = approval.TaskName is null
            ? approval.ToolName
            : $"{approval.ToolName} {glyphs.Dot} {approval.TaskName} ({approval.Ticket})";
        return new ConsoleChoiceRequest
        {
            Title = title,
            Description = approval.Reason,
            Detail = approval.Arguments,
            Question = waiting > 1 ? $"Allow this call? ({waiting} waiting)" : "Allow this call?",
            // No answers a line finished by habit: allowing takes a move of the operator's.
            DefaultValue = "n",
            Choices =
            [
                new ConsoleChoice("y", "Yes") { Shortcut = "y" },
                new ConsoleChoice("s", $"Yes, and allow {approval.ToolName} for this session") { Shortcut = "s" },
                new ConsoleChoice("n", "No") { Shortcut = "n" },
            ],
            Keys = $"Enter to confirm {glyphs.Dot} Esc to deny {glyphs.Dot} / for the prompt",
        };
    }
}
