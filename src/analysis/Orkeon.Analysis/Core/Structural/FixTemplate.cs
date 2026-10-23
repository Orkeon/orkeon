#pragma warning disable CA1032, CA1064
using System.Text.RegularExpressions;

namespace Orkeon.Analysis.Core.Structural;

internal sealed class UnboundMetavariableException(string name) : Exception($"The fix uses the metavariable '{name}', which the rule does not bind.")
{
    public string Name { get; } = name;
}

internal sealed class FixTemplate(string template)
{
    public string Template { get; } = template;

    public void Validate(IReadOnlyCollection<string> boundNames)
    {
        foreach (Match token in MetavariableGrammar.Token().Matches(Template))
        {
            var name = MetavariableGrammar.NameOf(token);
            if (name is not null && !boundNames.Contains(name)) throw new UnboundMetavariableException(name);
        }
    }

    public string Render(StructuralMatch match) => Render(match, "");

    /// <summary>Renders the fix; every continuation line of the template starts with <paramref name="indentation"/>, captured text is kept as is.</summary>
    public string Render(StructuralMatch match, string indentation)
    {
        var lines = Template.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\n" + indentation, StringComparison.Ordinal);
        return MetavariableGrammar.Token().Replace(lines, token => MetavariableGrammar.NameOf(token) is { } name
            ? match.Captures.GetValueOrDefault(name, "")
            : "$");
    }

}
