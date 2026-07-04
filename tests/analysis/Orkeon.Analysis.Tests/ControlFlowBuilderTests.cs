using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.TreeSitter;

namespace Orkeon.Analysis.Tests;

public class ControlFlowBuilderTests
{
    private static RaggableNode Extract(string source, string name)
    {
        using var pool = new TreeSitterParserPool();
        var mapper = new UniversalSemanticMapper(new TypeScriptAdapter(), pool);
        var nodes = mapper.ExtractNodes("/r/a.ts", source);
        return nodes.First(n => n.Name == name);
    }

    [Fact]
    public void Empty_body_returns_sentinel_graph()
    {
        var cfg = ControlFlowBuilder.Build([]);
        Assert.Equal(ControlFlowBuilder.ExitSentinel, cfg.EntryStatementId);
        Assert.Contains(ControlFlowBuilder.ExitSentinel, cfg.ExitStatementIds);
    }

    [Fact]
    public void Linear_method_has_sequential_edges()
    {
        var run = Extract(TestFixtures.LinearPipelineTs, "run");
        var cfg = ControlFlowBuilder.Build(run.Statements);
        Assert.Equal(run.Statements[0].Id, cfg.EntryStatementId);
        for (var i = 0; i < run.Statements.Count - 1; i++)
        {
            var src = run.Statements[i].Id;
            var dst = run.Statements[i + 1].Id;
            Assert.Contains(cfg.Edges, e => e.FromId == src && e.ToId == dst);
        }
    }

    [Fact]
    public void Return_reaches_exit()
    {
        var run = Extract(TestFixtures.LinearPipelineTs, "run");
        var cfg = ControlFlowBuilder.Build(run.Statements);
        var ret = run.Statements.First(s => s.Kind == StatementKind.Return);
        Assert.Contains(cfg.Edges, e => e.FromId == ret.Id && e.ToId == ControlFlowBuilder.ExitSentinel);
    }

    [Fact]
    public void If_statement_creates_two_branches()
    {
        const string src = """
            export class X {
                run(n: number): number {
                    if (n > 0) {
                        return 1;
                    } else {
                        return -1;
                    }
                }
            }
            """;
        var run = Extract(src, "run");
        var cfg = ControlFlowBuilder.Build(run.Statements);
        var ifStmt = run.Statements.First(s => s.Kind == StatementKind.If);
        var outgoing = cfg.Edges.Where(e => e.FromId == ifStmt.Id).ToList();
        Assert.Equal(2, outgoing.Count);
    }

    [Fact]
    public void Loop_creates_back_edge()
    {
        const string src = """
            export class X {
                run(items: number[]): number {
                    let sum = 0;
                    for (const i of items) {
                        sum = sum + i;
                    }
                    return sum;
                }
            }
            """;
        var run = Extract(src, "run");
        var cfg = ControlFlowBuilder.Build(run.Statements);
        var loop = run.Statements.First(s => s.Kind == StatementKind.ForOf);
        Assert.Contains(cfg.Edges, e => e.ToId == loop.Id && e.FromId != loop.Id);
    }
}
