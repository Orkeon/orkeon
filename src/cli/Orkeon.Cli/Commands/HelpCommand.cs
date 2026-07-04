using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Registry;
using Orkeon.Cli.Abstractions.Runners;

namespace Orkeon.Cli.Commands;

/// <summary>Lists available commands grouped by prefix. Reads the active runner registry from <see cref="RunnerContext.Current"/>.</summary>
public sealed class HelpCommand : IInteractiveCommand
{
    public string Name => "help";
    public IReadOnlyList<string> Aliases => new[] { "?", "h" };
    public string Description => "Show available commands";

    private sealed record HelpBlock(string Header, IReadOnlyList<IInteractiveCommand> Items, string? FallbackDescription);

    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var defaults = context.Scope.GetRequiredService<Registry.DefaultCommandRegistry>();
        var specific = RunnerContext.Current;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Available commands:");
        sb.AppendLine();

        var blocks = BuildBlocks(defaults, specific, sb);
        var leftWidth = ComputeLeftWidth(blocks);
        RenderBlocks(blocks, leftWidth, sb);

        context.Console.WriteLine(sb.ToString().TrimEnd());
        return Task.FromResult(CommandResult.Continue());
    }

    private static List<HelpBlock> BuildBlocks(
        Registry.DefaultCommandRegistry defaults,
        IInteractiveCommandRegistry? specific,
        StringBuilder sb)
    {
        var blocks = new List<HelpBlock>
        {
            new("[Default]", defaults.Commands.ToArray(), null),
        };

        if (specific is null)
        {
            sb.AppendLine("  (no specific runner active)");
            sb.AppendLine();
            return blocks;
        }

        // Group specific.Commands by first token
        var byGroup = specific.Commands
            .GroupBy(c =>
            {
                var idx = c.Name.IndexOf(' ', StringComparison.Ordinal);
                return idx >= 0 ? c.Name[..idx] : "(no group)";
            }, StringComparer.OrdinalIgnoreCase)
            // Real groups come before "(no group)"
            .OrderBy(g => g.Key == "(no group)" ? "￿" : g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var grp in byGroup)
        {
            var header = grp.Key == "(no group)" ? "[(no group)]" : $"[{grp.Key}]";
            blocks.Add(new(header, grp.ToArray(), null));
        }

        if (specific.Fallback is not null)
        {
            blocks.Add(new("[Default action]", Array.Empty<IInteractiveCommand>(), specific.Fallback.Description));
        }

        return blocks;
    }

    private static int ComputeLeftWidth(IReadOnlyList<HelpBlock> blocks)
    {
        int leftWidth = 0;
        foreach (var block in blocks)
        {
            foreach (var cmd in block.Items)
            {
                var aliasText = FormatNameWithAliases(cmd);
                if (aliasText.Length > leftWidth) leftWidth = aliasText.Length;
            }
            if (block.FallbackDescription is not null)
            {
                const string label = "(any other input)";
                if (label.Length > leftWidth) leftWidth = label.Length;
            }
        }
        return leftWidth + 2;
    }

    private static void RenderBlocks(IReadOnlyList<HelpBlock> blocks, int leftWidth, StringBuilder sb)
    {
        foreach (var block in blocks)
        {
            sb.Append("  ").AppendLine(block.Header);
            foreach (var cmd in block.Items)
            {
                var left = FormatNameWithAliases(cmd);
                sb.Append("    ").Append(left.PadRight(leftWidth)).AppendLine(cmd.Description);
            }
            if (block.FallbackDescription is not null)
            {
                sb.Append("    ").Append("(any other input)".PadRight(leftWidth)).AppendLine(block.FallbackDescription);
            }
            sb.AppendLine();
        }
    }

    private static string FormatNameWithAliases(IInteractiveCommand cmd)
    {
        if (cmd.Aliases.Count == 0) return cmd.Name;
        return cmd.Name + ", " + string.Join(", ", cmd.Aliases);
    }
}
