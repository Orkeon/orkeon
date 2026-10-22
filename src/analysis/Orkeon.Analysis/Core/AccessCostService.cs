using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Core;

public sealed partial class AccessCostService
{
    public static Regex DefaultReceivers { get; } = BuildDefaultReceivers();

    [GeneratedRegex(
        @"repositor|service|wrapper|client|provider|unitofwork|dbcontext|dbset|gateway|store|" +
        @"publisher|bus|mediator|cache|queue|adapter|channel|hub|sandbox|filesystem|" +
        @"^(this\.)?_?(db|context|fs)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex BuildDefaultReceivers();

    [GeneratedRegex(@"^(Save|Add|Insert|Update|Delete|Remove|Persist|Publish|Send|Commit|Create|Put|Post|Write|Upsert|Store|Execute|Transfer|Append|Set|Enqueue|Dispatch|Ingest)")]
    private static partial Regex WriteVerbs();

    [GeneratedRegex(@"[()]|\.(\w+)\s*(?:<[^>]*>)?\s*(?=\()")]
    private static partial Regex Calls();

    [GeneratedRegex(@"^\(\s*await\b.*?\)\s*\.(Where|Any|All|First|FirstOrDefault|Single|SingleOrDefault|Count|Last|LastOrDefault|Contains|OrderBy|OrderByDescending|Skip|Take)\s*(?:<[^>]*>)?\s*\(")]
    private static partial Regex MemoryFilter();

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Public service API, instance by contract.")]
    public AccessCostReport Compute(IEnumerable<RaggableNode> members, Regex receivers)
    {
        var costs = members.Select(m => Measure(m, receivers)).ToImmutableList();
        return new AccessCostReport
        {
            Members = costs,
            Cost = Sum(costs.Select(c => c.Cost)),
        };
    }

    private static MemberAccessCost Measure(RaggableNode member, Regex receivers)
    {
        var calls = ImmutableList.CreateBuilder<PortCall>();
        var filters = ImmutableList.CreateBuilder<InMemoryFilter>();
        foreach (var statement in Flatten(member.Statements))
        {
            if (statement.Kind == StatementKind.Await && statement.Receiver is { } receiver && receivers.IsMatch(receiver))
                calls.Add(new PortCall
                {
                    Receiver = receiver,
                    Method = MethodOf(statement.Expression),
                    Line = statement.StartLine,
                    InLoop = statement.InLoop,
                    InLambda = statement.InLambda,
                });
            else if (statement.Kind == StatementKind.FunctionCall
                     && MemoryFilter().Match(statement.Expression) is { Success: true } filter
                     && EndsWithCall(statement.Expression, filter.Length))
                filters.Add(new InMemoryFilter { Line = statement.StartLine, Method = filter.Groups[1].Value });
        }

        var flat = calls.Where(c => !c.InLoop && !c.InLambda).ToList();
        return new MemberAccessCost
        {
            Member = member.Name,
            PortCalls = calls.ToImmutable(),
            InMemoryFilters = filters.ToImmutable(),
            Cost = new CostLine
            {
                Reads = flat.Count(c => !WriteVerbs().IsMatch(c.Method)),
                Writes = flat.Count(c => WriteVerbs().IsMatch(c.Method)),
                Loops = calls.Count(c => c.InLoop),
                Lambdas = calls.Count(c => !c.InLoop && c.InLambda),
                MemoryFilters = filters.Count,
            },
        };
    }

    private static CostLine Sum(IEnumerable<CostLine> lines)
    {
        var all = lines.ToList();
        return new CostLine
        {
            Reads = all.Sum(c => c.Reads),
            Writes = all.Sum(c => c.Writes),
            Loops = all.Sum(c => c.Loops),
            Lambdas = all.Sum(c => c.Lambdas),
            MemoryFilters = all.Sum(c => c.MemoryFilters),
        };
    }

    private static IEnumerable<StatementNode> Flatten(IEnumerable<StatementNode> statements)
        => statements.SelectMany(s => Flatten(s.Children).Prepend(s));

    private static string MethodOf(string expression)
    {
        var depth = 0;
        string? last = null;
        foreach (Match m in Calls().Matches(expression))
        {
            if (m.Value == "(") depth++;
            else if (m.Value == ")") depth--;
            else if (depth == 0) last = m.Groups[1].Value;
        }

        return last ?? "?";
    }

    /// <summary>True when the call whose arguments open at <paramref name="openedAt"/> closes the expression: the filter is the outermost call.</summary>
    private static bool EndsWithCall(string expression, int openedAt)
    {
        var depth = 1;
        for (var i = openedAt; i < expression.Length; i++)
        {
            depth += expression[i] switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth == 0) return i == expression.Length - 1;
        }

        return false;
    }
}
