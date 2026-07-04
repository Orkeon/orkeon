using System.Text.Json;
using Orkeon.Cli.Abstractions.Commands;

namespace Orkeon.Examples.Interactive.InterviewSpecForge.Commands;

/// <summary>
/// Renders <c>topics/_graph.json</c> as a readable list:
/// for each node (slug → label, weight, nb sessions).
/// </summary>
/// <remarks>
/// // EXCEPTION-BOOTSTRAP — read-only.
/// </remarks>
public sealed class TopicsCommand : IInteractiveCommand
{
    private readonly TranscriptsCatalog _catalog;

    public TopicsCommand(TranscriptsCatalog catalog) => _catalog = catalog;

    public string Name => "topics";
    public IReadOnlyList<string> Aliases => new[] { "t" };
    public string Description => "Render the current topics/_graph.json (nodes + edges)";

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var graphPath = Path.Combine(_catalog.TopicsRoot, "_graph.json");
        if (!File.Exists(graphPath))
        {
            context.Console.WriteLine($"  (no graph yet at {graphPath} — run 'forge <slot>' first)");
            return CommandResult.Continue();
        }

        var raw = await File.ReadAllTextAsync(graphPath, cancellationToken).ConfigureAwait(false);
        JsonDocument doc;
        try { doc = JsonDocument.Parse(raw); }
        catch (JsonException ex)
        {
            return CommandResult.Continue($"  malformed graph JSON: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            context.Console.WriteLine("");
            if (root.TryGetProperty("version", out var v))
                context.Console.WriteLine($"  Graph version: {v}");
            if (root.TryGetProperty("last_updated_at", out var lu))
                context.Console.WriteLine($"  Last updated : {lu}");
            context.Console.WriteLine("");
            context.Console.WriteLine("  Nodes:");

            if (root.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Object)
            {
                foreach (var node in nodes.EnumerateObject())
                {
                    var slug = node.Name;
                    var n = node.Value;
                    var label = n.TryGetProperty("label", out var lab) ? lab.GetString() ?? "" : "";
                    var weight = n.TryGetProperty("weight", out var w) ? w.GetRawText() : "?";
                    var sessions = 0;
                    if (n.TryGetProperty("sessions_associees", out var s) && s.ValueKind == JsonValueKind.Array)
                        sessions = s.GetArrayLength();
                    context.Console.WriteLine($"    [{weight,3}]  {slug,-32}  {label}  ({sessions} sess.)");
                }
            }

            if (root.TryGetProperty("edges", out var edges) && edges.ValueKind == JsonValueKind.Array)
            {
                context.Console.WriteLine("");
                context.Console.WriteLine($"  Edges ({edges.GetArrayLength()}):");
                foreach (var e in edges.EnumerateArray())
                {
                    var from = e.TryGetProperty("from", out var f) ? f.GetString() : "?";
                    var to   = e.TryGetProperty("to",   out var t) ? t.GetString() : "?";
                    var type = e.TryGetProperty("type", out var ty) ? ty.GetString() : "?";
                    var wgt  = e.TryGetProperty("weight", out var wg) ? wg.GetRawText() : "?";
                    context.Console.WriteLine($"    {from} --[{type,-11}]--> {to}  (w={wgt})");
                }
            }
            context.Console.WriteLine("");
        }

        return CommandResult.Continue();
    }
}
