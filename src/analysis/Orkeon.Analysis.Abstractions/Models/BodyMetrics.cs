namespace Orkeon.Analysis.Abstractions.Models;

public record BodyMetrics
{
    public required int StatementCount { get; init; }
    public required int MaxDepth { get; init; }
    public required int BranchCount { get; init; }
    public required int LoopCount { get; init; }
    public required int TryCatchCount { get; init; }
    public required int CallCount { get; init; }
    public required int EarlyReturnCount { get; init; }
    public required int AwaitCount { get; init; }
    public required int ClosureCount { get; init; }
    public required int CyclomaticComplexity { get; init; }
    public bool IsPartial { get; init; }

    public static BodyMetrics Compute(IReadOnlyList<StatementNode> statements)
        => Compute(statements, isPartial: false);

    public static BodyMetrics Compute(IReadOnlyList<StatementNode> statements, bool isPartial)
    {
        ArgumentNullException.ThrowIfNull(statements);

        var counters = new Counters();
        foreach (var s in statements) Walk(s, depth: 0, counters);

        var branches = counters.BranchCount;
        return new BodyMetrics
        {
            StatementCount = counters.Total,
            MaxDepth = counters.MaxDepth,
            BranchCount = branches,
            LoopCount = counters.LoopCount,
            TryCatchCount = counters.TryCatchCount,
            CallCount = counters.CallCount,
            EarlyReturnCount = counters.EarlyReturnCount,
            AwaitCount = counters.AwaitCount,
            ClosureCount = counters.ClosureCount,
            CyclomaticComplexity = branches + 1,
            IsPartial = isPartial,
        };
    }

    private sealed class Counters
    {
        public int Total;
        public int MaxDepth;
        public int BranchCount;
        public int LoopCount;
        public int TryCatchCount;
        public int CallCount;
        public int EarlyReturnCount;
        public int AwaitCount;
        public int ClosureCount;
    }

    private static void Walk(StatementNode s, int depth, Counters c)
    {
        c.Total++;
        if (depth > c.MaxDepth) c.MaxDepth = depth;

        switch (s.Kind)
        {
            case StatementKind.If:
            case StatementKind.ElseIf:
            case StatementKind.SwitchCase:
                c.BranchCount++;
                break;
            case StatementKind.For:
            case StatementKind.ForOf:
            case StatementKind.ForIn:
            case StatementKind.While:
            case StatementKind.DoWhile:
                c.LoopCount++;
                c.BranchCount++;
                break;
            case StatementKind.TryCatch:
                c.TryCatchCount++;
                c.BranchCount++;
                break;
            case StatementKind.FunctionCall:
            case StatementKind.MethodCall:
            case StatementKind.ChainedCall:
            case StatementKind.ConstructorCall:
                c.CallCount++;
                break;
            case StatementKind.Return:
            case StatementKind.EarlyReturn:
                if (depth > 0) c.EarlyReturnCount++;
                break;
            case StatementKind.Await:
                c.AwaitCount++;
                break;
            case StatementKind.ArrowFunction:
            case StatementKind.Closure:
            case StatementKind.Callback:
                c.ClosureCount++;
                break;
        }

        foreach (var child in s.Children) Walk(child, depth + 1, c);
    }
}
