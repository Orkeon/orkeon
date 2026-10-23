using Orkeon.Analysis.Core.Editing;
using Orkeon.Analysis.Core.Editing.Intent;

namespace Orkeon.Tools.Analysis;

/// <summary>Builds the edit operation of an intent <c>op</c> (ensure_using, ensure_import, ensure_field, ensure_member, ensure_section, insert_under_heading, replace_mermaid); shared by edit_batch and node_ensure.</summary>
internal static class IntentOperations
{
    public static IReadOnlyList<string> Names { get; } =
        ["ensure_using", "ensure_import", "ensure_field", "ensure_member", "ensure_section", "insert_under_heading", "replace_mermaid"];

    public static IEditOperation Create(string? op, string? selector, string? name, string? module, string? text, int? index = null, int? level = null)
    {
        switch (op)
        {
            case "ensure_using":
            case "ensure_import":
                return new EnsureUsingOperation(Required(name, "name", op), string.IsNullOrEmpty(module) ? null : module);
            case "ensure_field":
            case "ensure_member":
                return new EnsureMemberOperation(
                    NodeSelector.Parse(Required(selector, "selector", op)),
                    Required(text, "text", op),
                    op == "ensure_field" ? EnsureMemberKind.Field : EnsureMemberKind.Member);
            case "ensure_section":
                return new EnsureSectionOperation(Required(name, "name", op), level);
            case "insert_under_heading":
                return new InsertUnderHeadingOperation(NodeSelector.Parse(Required(selector, "selector", op)), Required(text, "text", op));
            case "replace_mermaid":
                return new ReplaceMermaidOperation(
                    NodeSelector.Parse(Required(selector, "selector", op)),
                    index ?? throw new ArgumentException($"index is required for {op}.", nameof(index)),
                    Required(text, "text", op));
            default:
                throw new ArgumentException(
                    $"op '{op}' is not supported; use ensure_using, ensure_import, ensure_field, ensure_member, ensure_section, insert_under_heading or replace_mermaid.", nameof(op));
        }
    }

    private static string Required(string? value, string field, string op)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{field} is required for {op}.", field) : value;
}
