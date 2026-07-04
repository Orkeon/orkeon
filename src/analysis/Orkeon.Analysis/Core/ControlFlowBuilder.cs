using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Core;

public static class ControlFlowBuilder
{
    public const string ExitSentinel = "$exit";

    public static ControlFlowGraph Build(IReadOnlyList<StatementNode> statements)
    {
        ArgumentNullException.ThrowIfNull(statements);

        if (statements.Count == 0)
        {
            return new ControlFlowGraph([], ExitSentinel, [ExitSentinel]);
        }

        var edges = new List<ControlFlowEdge>();
        var exits = new List<string>();
        var entry = statements[0].Id;

        for (var i = 0; i < statements.Count; i++)
        {
            var current = statements[i];
            var next = i + 1 < statements.Count ? statements[i + 1].Id : ExitSentinel;
            EmitEdgesForStatement(current, next, edges, exits);
        }

        if (exits.Count == 0) exits.Add(ExitSentinel);

        return new ControlFlowGraph(edges, entry, exits);
    }

    private static void EmitEdgesForStatement(
        StatementNode stmt, string nextId, List<ControlFlowEdge> edges, List<string> exits)
    {
        switch (stmt.Kind)
        {
            case StatementKind.If:
            case StatementKind.ElseIf:
                EmitIfEdges(stmt, nextId, edges, exits);
                break;
            case StatementKind.Switch:
                EmitSwitchEdges(stmt, nextId, edges, exits);
                break;
            case StatementKind.For:
            case StatementKind.ForOf:
            case StatementKind.ForIn:
            case StatementKind.While:
            case StatementKind.DoWhile:
                EmitLoopEdges(stmt, nextId, edges, exits);
                break;
            case StatementKind.Return:
            case StatementKind.EarlyReturn:
            case StatementKind.Throw:
                edges.Add(new ControlFlowEdge(stmt.Id, ExitSentinel, null));
                if (!exits.Contains(stmt.Id)) exits.Add(stmt.Id);
                break;
            case StatementKind.Break:
            case StatementKind.Continue:
                edges.Add(new ControlFlowEdge(stmt.Id, nextId, null));
                break;
            case StatementKind.TryCatch:
                EmitTryCatchEdges(stmt, nextId, edges, exits);
                break;
            default:
                edges.Add(new ControlFlowEdge(stmt.Id, nextId, null));
                RecurseChildren(stmt, nextId, edges, exits);
                break;
        }
    }

    private static void EmitIfEdges(
        StatementNode stmt, string nextId, List<ControlFlowEdge> edges, List<string> exits)
    {
        var condition = stmt.Condition;
        var (thenId, elseId) = ResolveIfBranches(stmt);
        edges.Add(new ControlFlowEdge(stmt.Id, thenId ?? nextId, condition));
        edges.Add(new ControlFlowEdge(stmt.Id, elseId ?? nextId, condition is null ? "!" : $"!({condition})"));
        RecurseChildren(stmt, nextId, edges, exits);
    }

    private static void EmitSwitchEdges(
        StatementNode stmt, string nextId, List<ControlFlowEdge> edges, List<string> exits)
    {
        foreach (var c in stmt.Children)
        {
            edges.Add(new ControlFlowEdge(stmt.Id, c.Id, null));
        }
        if (stmt.Children.Count == 0)
            edges.Add(new ControlFlowEdge(stmt.Id, nextId, null));
        RecurseChildren(stmt, nextId, edges, exits);
    }

    private static void EmitLoopEdges(
        StatementNode stmt, string nextId, List<ControlFlowEdge> edges, List<string> exits)
    {
        edges.Add(new ControlFlowEdge(stmt.Id, nextId, null));
        if (stmt.Children.Count == 0) return;

        edges.Add(new ControlFlowEdge(stmt.Id, stmt.Children[0].Id, stmt.Condition));
        var bodyLast = LastDescendant(stmt.Children);
        if (bodyLast is not null)
            edges.Add(new ControlFlowEdge(bodyLast.Id, stmt.Id, null));
        RecurseChildren(stmt, stmt.Id, edges, exits);
    }

    private static void EmitTryCatchEdges(
        StatementNode stmt, string nextId, List<ControlFlowEdge> edges, List<string> exits)
    {
        if (stmt.Children.Count > 0)
            edges.Add(new ControlFlowEdge(stmt.Id, stmt.Children[0].Id, null));
        foreach (var c in stmt.Children)
        {
            if (c.Kind == StatementKind.Catch || c.Kind == StatementKind.Finally)
                edges.Add(new ControlFlowEdge(stmt.Id, c.Id, c.Kind == StatementKind.Catch ? "throw" : null));
        }
        edges.Add(new ControlFlowEdge(stmt.Id, nextId, null));
        RecurseChildren(stmt, nextId, edges, exits);
    }

    private static void RecurseChildren(
        StatementNode parent, string nextId, List<ControlFlowEdge> edges, List<string> exits)
    {
        for (var i = 0; i < parent.Children.Count; i++)
        {
            var child = parent.Children[i];
            var childNext = i + 1 < parent.Children.Count ? parent.Children[i + 1].Id : nextId;
            EmitEdgesForStatement(child, childNext, edges, exits);
        }
    }

    private static (string? ThenId, string? ElseId) ResolveIfBranches(StatementNode ifStmt)
    {
        string? thenId = null;
        string? elseId = null;
        foreach (var c in ifStmt.Children)
        {
            if (c.Kind is StatementKind.Else or StatementKind.ElseIf)
            {
                elseId ??= c.Id;
            }
            else
            {
                thenId ??= c.Id;
            }
        }
        return (thenId, elseId);
    }

    private static StatementNode? LastDescendant(IReadOnlyList<StatementNode> statements)
    {
        if (statements.Count == 0) return null;
        var last = statements[^1];
        return last.Children.Count > 0 ? LastDescendant(last.Children) ?? last : last;
    }
}
