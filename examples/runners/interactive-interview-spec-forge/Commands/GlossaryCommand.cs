using System.Text.Json;
using Orkeon.Cli.Abstractions.Commands;

namespace Orkeon.Examples.Interactive.InterviewSpecForge.Commands;

/// <summary>
/// Renders <c>glossary/GLOSSAIRE_METIER.json</c>. With an optional term
/// argument, narrows the output to one entry (definition + aliases +
/// sessions associated).
/// </summary>
/// <remarks>
/// // EXCEPTION-BOOTSTRAP — read-only.
/// </remarks>
public sealed class GlossaryCommand : IInteractiveCommand
{
    private readonly TranscriptsCatalog _catalog;

    public GlossaryCommand(TranscriptsCatalog catalog) => _catalog = catalog;

    public string Name => "glossary";
    public IReadOnlyList<string> Aliases => new[] { "g" };
    public string Description => "glossary [<term>] — list the glossary, or show one term";

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_catalog.GlossaryRoot, "GLOSSAIRE_METIER.json");
        if (!File.Exists(path))
        {
            context.Console.WriteLine($"  (no glossary yet at {path} — run 'forge <slot>' first)");
            return CommandResult.Continue();
        }

        var raw = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        JsonDocument doc;
        try { doc = JsonDocument.Parse(raw); }
        catch (JsonException ex)
        {
            return CommandResult.Continue($"  malformed glossary JSON: {ex.Message}");
        }

        var target = context.Args is [var first, ..] ? first : null;

        using (doc)
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("terms", out var terms) || terms.ValueKind != JsonValueKind.Object)
            {
                context.Console.WriteLine("  glossary has no 'terms' object.");
                return CommandResult.Continue();
            }

            if (target is null)
            {
                context.Console.WriteLine("");
                if (root.TryGetProperty("version", out var ver))
                    context.Console.WriteLine($"  Glossary version: {ver}");
                context.Console.WriteLine("");
                context.Console.WriteLine("  Terms:");
                foreach (var t in terms.EnumerateObject())
                {
                    var aliases = t.Value.TryGetProperty("aliases", out var a) && a.ValueKind == JsonValueKind.Array
                        ? a.GetArrayLength() : 0;
                    var sessions = t.Value.TryGetProperty("sessions_associees", out var s) && s.ValueKind == JsonValueKind.Array
                        ? s.GetArrayLength() : 0;
                    var domaine = t.Value.TryGetProperty("domaine", out var d) ? d.GetString() ?? "?" : "?";
                    context.Console.WriteLine($"    {t.Name,-32}  [{domaine,-11}]  aliases={aliases} sessions={sessions}");
                }
                context.Console.WriteLine("");
                return CommandResult.Continue();
            }

            if (!terms.TryGetProperty(target, out var hit))
            {
                return CommandResult.Continue($"  unknown term '{target}'. Run 'glossary' (no args) to list.");
            }

            context.Console.WriteLine("");
            context.Console.WriteLine($"  Term: {target}");
            foreach (var prop in hit.EnumerateObject())
            {
                context.Console.WriteLine($"    {prop.Name,-22}: {prop.Value}");
            }
            context.Console.WriteLine("");
        }

        return CommandResult.Continue();
    }
}
