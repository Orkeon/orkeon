using System.Diagnostics.CodeAnalysis;
using Orkeon.Analysis.TreeSitter;
using TsNode = TreeSitter.Node;

namespace Orkeon.Analysis.Core.Editing;

/// <summary>Parameter types of a C# or TypeScript callable, read from its tree-sitter node.</summary>
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
            .Where(p => p.IsNamed && p.Type != "comment")
            .Select(TypeOf)
            .ToList();
        return true;
    }

    private static string TypeOf(TsNode parameter)
    {
        var type = parameter.ChildByField("type");
        if (type is null) return "any";
        return NodeSelector.Compact(type.Text ?? string.Empty).TrimStart(':');
    }
}
