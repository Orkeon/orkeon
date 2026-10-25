using TsNode = TreeSitter.Node;

namespace Orkeon.Analysis.Adapters;

/// <summary>
/// The declarator chain C and C++ share: a name sits under nested <c>declarator</c> fields
/// (<c>int *f(void)</c>: pointer_declarator, function_declarator, identifier).
/// </summary>
internal static class CDeclarators
{
    private static readonly HashSet<string> NameTypes = new(StringComparer.Ordinal)
    {
        "identifier", "field_identifier", "type_identifier", "destructor_name", "operator_name", "primitive_type",
    };

    /// <summary>The innermost declarator of <paramref name="node"/>: its name, or a qualified name.</summary>
    public static TsNode? Innermost(TsNode node)
    {
        var current = node.GetChildForField("declarator");
        while (current is not null)
        {
            if (NameTypes.Contains(current.Type) || current.Type == "qualified_identifier") return current;
            current = current.GetChildForField("declarator") ?? current.NamedChildren.FirstOrDefault(c => c.Type != "type_qualifier");
        }
        return null;
    }

    /// <summary>The declared name, the last segment of a qualified one (<c>Circle::area</c> is <c>area</c>).</summary>
    public static string NameOf(TsNode node)
    {
        var declarator = Innermost(node);
        while (declarator?.Type == "qualified_identifier") declarator = declarator.GetChildForField("name");
        return declarator?.Text ?? string.Empty;
    }

    /// <summary>Whether the declarator chain of <paramref name="node"/> declares a function.</summary>
    public static bool DeclaresFunction(TsNode node)
    {
        for (var current = node.GetChildForField("declarator"); current is not null; current = current.GetChildForField("declarator"))
        {
            if (current.Type == "function_declarator") return true;
        }
        return false;
    }

    /// <summary>The <c>&lt;system&gt;</c> or <c>"local"</c> target of an <c>#include</c>.</summary>
    public static string? ResolveInclude(string includePath, string currentVirtualFilePath)
    {
        var trimmed = includePath.Trim();
        if (trimmed.StartsWith('<')) return $"ext::system::{trimmed.Trim('<', '>')}";
        var local = AdapterText.Unquote(trimmed);
        return local.Length == 0 ? null : AdapterText.JoinRelative(currentVirtualFilePath, local);
    }
}
