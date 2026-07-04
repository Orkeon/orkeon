using System.Text;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Console;
using TgApp = Terminal.Gui.App.Application;

namespace Orkeon.Examples.Interactive.ClaimVerification.Commands;

/// <summary>
/// Interactively creates a new <c>claim-NNN-&lt;slug&gt;.md</c> in the corpus.
/// In TUI mode, pops a single modal dialog with all four fields
/// (title, verbatim, context, why-selected); in plain mode falls back to
/// sequential console prompts.
/// </summary>
/// <remarks>
/// // EXCEPTION-BOOTSTRAP — writes to the claim corpus on the physical
/// filesystem (the corpus lives outside any per-verify VFS mount).
/// </remarks>
public sealed class AddClaimCommand : IInteractiveCommand
{
    private readonly ClaimsCatalog _catalog;
    private readonly ConsoleInputService _input;

    public AddClaimCommand(ClaimsCatalog catalog, ConsoleInputService input)
    {
        _catalog = catalog;
        _input = input;
    }

    public string Name => "add-claim";
    public IReadOnlyList<string> Aliases => new[] { "add" };
    public string Description => "Create a new claim file interactively";

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var slot = _catalog.NextFreeSlot();

        string title, claimVerbatim, contextStr, probe;
        if (TgApp.Initialized)
        {
            var dlgResult = PromptViaDialog(slot);
            if (dlgResult is null)
            {
                context.Console.WriteLine("  (cancelled)");
                return CommandResult.Continue();
            }
            (title, claimVerbatim, contextStr, probe) = dlgResult.Value;
            if (string.IsNullOrWhiteSpace(title))
                return CommandResult.Continue("  ERROR: title is required");
            if (string.IsNullOrWhiteSpace(claimVerbatim))
                return CommandResult.Continue("  ERROR: claim verbatim is required");
        }
        else
        {
            title = _input.GetRequiredString($"  Title (short, e.g. \"Earth is flat\"): ");
            claimVerbatim = _input.GetRequiredString("  Claim verbatim (one sentence in quotes form): ");
            contextStr = _input.GetOptionalString("  Context (optional, single line — Enter to skip): ") ?? "";
            probe = _input.GetOptionalString("  Why selected (optional, single line — Enter to skip): ") ?? "";
        }

        var slug = Slugify(title);
        var fileName = $"claim-{slot}-{slug}.md";
        var filePath = Path.Combine(_catalog.ClaimsRoot, fileName);

        if (File.Exists(filePath))
            return CommandResult.Continue($"  ERROR: target file already exists: {filePath}");

        var sb = new StringBuilder();
        sb.AppendLine($"# Claim {slot} — {title}");
        sb.AppendLine();
        sb.Append("> ").AppendLine(claimVerbatim);
        sb.AppendLine();
        sb.AppendLine("## Context");
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(contextStr) ? "(to be written)" : contextStr);
        sb.AppendLine();
        sb.AppendLine("## Why this claim was selected for the experiment");
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(probe) ? "(to be written)" : probe);
        sb.AppendLine();

        await File.WriteAllTextAsync(filePath, sb.ToString(), cancellationToken).ConfigureAwait(false);

        context.Console.WriteLine("");
        context.Console.WriteLine($"  Created: {filePath}");
        context.Console.WriteLine($"  Run with: verify {slot}");
        context.Console.WriteLine("");

        return CommandResult.Continue();
    }

    /// <summary>
    /// Pops the <see cref="AddClaimDialog"/> on the Terminal.Gui UI thread and
    /// blocks until the user submits or cancels. Returns null on cancel.
    /// </summary>
    private static (string title, string verbatim, string context, string probe)?
        PromptViaDialog(string suggestedSlot)
    {
        var tcs = new TaskCompletionSource<(string, string, string, string)?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        TgApp.Invoke(() =>
        {
            try
            {
                var dlg = new AddClaimDialog(suggestedSlot);
                TgApp.Run(dlg);
                tcs.TrySetResult(dlg.Cancelled
                    ? null
                    : (dlg.TitleText, dlg.Verbatim, dlg.ContextText, dlg.WhySelected));
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        return tcs.Task.GetAwaiter().GetResult();
    }

    private static string Slugify(string title)
    {
        var lower = title.Trim().ToLowerInvariant();
        var sb = new StringBuilder(lower.Length);
        char prev = '\0';
        foreach (var ch in lower)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                prev = ch;
            }
            else if (prev != '-' && sb.Length > 0)
            {
                sb.Append('-');
                prev = '-';
            }
        }
        var slug = sb.ToString().Trim('-');
        return string.IsNullOrEmpty(slug) ? "untitled" : slug;
    }
}
