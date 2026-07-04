using System.Text.Json;
using Orkeon.Cli.Abstractions.Commands;

namespace Orkeon.Examples.Interactive.InterviewSpecForge.Commands;

/// <summary>
/// Renders <c>tasks/_index.json</c>; supports <c>--status &lt;STATUS&gt;</c> filtering.
/// Allowed statuses (D15): TODO | IN_PROGRESS | DONE | MERGED | DEPRECATED | BLOCKED.
/// </summary>
/// <remarks>
/// // EXCEPTION-BOOTSTRAP — read-only.
/// </remarks>
public sealed class TasksCommand : IInteractiveCommand
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "TODO", "IN_PROGRESS", "DONE", "MERGED", "DEPRECATED", "BLOCKED",
    };

    private readonly TranscriptsCatalog _catalog;

    public TasksCommand(TranscriptsCatalog catalog) => _catalog = catalog;

    public string Name => "tasks";
    public IReadOnlyList<string> Aliases => new[] { "tk" };
    public string Description => "tasks [--status TODO|IN_PROGRESS|DONE|MERGED|DEPRECATED|BLOCKED] — list tasks";

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        string? statusFilter = null;
        for (int i = 0; i < context.Args.Count; i++)
        {
            if (string.Equals(context.Args[i], "--status", StringComparison.OrdinalIgnoreCase)
                && i + 1 < context.Args.Count)
            {
                statusFilter = context.Args[i + 1];
                if (!AllowedStatuses.Contains(statusFilter))
                    return CommandResult.Continue($"  invalid status '{statusFilter}'. Allowed: {string.Join('|', AllowedStatuses)}.");
                break;
            }
        }

        var indexPath = Path.Combine(_catalog.TasksRoot, "_index.json");
        if (!File.Exists(indexPath))
        {
            context.Console.WriteLine($"  (no task index yet at {indexPath} — run 'forge <slot>' first)");
            return CommandResult.Continue();
        }

        var raw = await File.ReadAllTextAsync(indexPath, cancellationToken).ConfigureAwait(false);
        JsonDocument doc;
        try { doc = JsonDocument.Parse(raw); }
        catch (JsonException ex)
        {
            return CommandResult.Continue($"  malformed task index JSON: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("tasks", out var tasks) || tasks.ValueKind != JsonValueKind.Object)
            {
                context.Console.WriteLine("  task index has no 'tasks' object.");
                return CommandResult.Continue();
            }

            context.Console.WriteLine("");
            context.Console.WriteLine($"  Tasks{(statusFilter is null ? "" : $" (status={statusFilter.ToUpperInvariant()})")}:");
            context.Console.WriteLine("");
            int count = 0;
            foreach (var t in tasks.EnumerateObject())
            {
                var status = t.Value.TryGetProperty("status", out var s) ? s.GetString() ?? "TODO" : "TODO";
                if (statusFilter is not null
                    && !string.Equals(status, statusFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                var slug = t.Name;
                var id   = t.Value.TryGetProperty("id_stable", out var ist) ? ist.GetString() ?? "" : "";
                var prio = t.Value.TryGetProperty("priority", out var p) ? p.GetString() ?? "medium" : "medium";
                var title = t.Value.TryGetProperty("title", out var ti) ? ti.GetString() ?? "" : "";
                context.Console.WriteLine($"    [{status,-11}] [{prio,-6}] {slug,-40} {title}");
                if (!string.IsNullOrEmpty(id))
                    context.Console.WriteLine($"                                  id={id}");
                count++;
            }
            context.Console.WriteLine("");
            context.Console.WriteLine($"  ({count} task(s) shown)");
            context.Console.WriteLine("");
        }

        return CommandResult.Continue();
    }
}
