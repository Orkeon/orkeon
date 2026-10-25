using System.Diagnostics.CodeAnalysis;
using Orkeon.Analysis.TreeSitter;
using TsNode = TreeSitter.Node;

namespace Orkeon.Analysis.Core.Editing;

/// <summary>Parameter types of a callable, read from the <c>parameters</c> field of its tree-sitter node (any of them untyped is <c>any</c>).</summary>
internal static class SignatureExtractor
{
    /// <summary>False when the node has no parameter list at all (a type or a property, not a callable).</summary>
    public static bool TryGetParameterTypes(TsNode callable, [NotNullWhen(true)] out IReadOnlyList<string>? types)
    {
        var parameters = callable.ChildByField("parameters");
        if (parameters is null)
        {
            types = null;
            return false;
        }

        types = parameters.Children
            .Where(p => p.IsNamed && !StructuralFile.IsComment(p.Type) && !IsReceiver(p))
            .Select(TypeOf)
            .ToList();
        return true;
    }

    // The receiver a method declares as its first parameter: Rust's self, Python's self or cls.
    private static bool IsReceiver(TsNode parameter)
        => parameter.Type == "self_parameter" || (parameter.Type == "identifier" && parameter.Text is "self" or "cls");

    private static string TypeOf(TsNode parameter)
    {
        var type = parameter.ChildByField("type");
        if (type is null) return "any";
        return NodeSelector.Compact(type.Text ?? string.Empty).TrimStart(':');
    }
}
