using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Core;

public static class DataFlowAnalyzer
{
    public static IReadOnlyList<DataFlowLink> Analyze(IReadOnlyList<StatementNode> statements)
    {
        ArgumentNullException.ThrowIfNull(statements);

        var links = new List<DataFlowLink>();
        var lastWriter = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var stmt in Flatten(statements))
        {
            foreach (var r in stmt.References)
            {
                switch (r.Kind)
                {
                    case ReferenceKind.Read:
                        if (lastWriter.TryGetValue(r.Name, out var sourceId) && sourceId != stmt.Id)
                            links.Add(new DataFlowLink(r.Name, sourceId, stmt.Id));
                        break;
                    case ReferenceKind.Write:
                        lastWriter[r.Name] = stmt.Id;
                        break;
                }
            }
        }
        return links;
    }

    private static IEnumerable<StatementNode> Flatten(IReadOnlyList<StatementNode> statements)
    {
        foreach (var s in statements)
        {
            yield return s;
            foreach (var child in Flatten(s.Children)) yield return child;
        }
    }
}
