using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.TreeSitter;

namespace Orkeon.Analysis.Tests;

public class DataFlowAnalyzerTests
{
    private static RaggableNode Extract(string source, string name)
    {
        using var pool = new TreeSitterParserPool();
        var mapper = new UniversalSemanticMapper(new TypeScriptAdapter(), pool);
        var nodes = mapper.ExtractNodes("/r/a.ts", source);
        return nodes.First(n => n.Name == name);
    }

    [Fact]
    public void Write_then_read_creates_link()
    {
        const string src = """
            export class X {
                run(): number {
                    const a = 10;
                    return compute(a);
                }
            }
            """;
        var run = Extract(src, "run");
        var links = DataFlowAnalyzer.Analyze(run.Statements);
        Assert.Contains(links, l => l.VariableName == "a");
    }

    [Fact]
    public void Variable_never_read_produces_no_link()
    {
        const string src = """
            export class X {
                run(): void {
                    const unused = 42;
                }
            }
            """;
        var run = Extract(src, "run");
        var links = DataFlowAnalyzer.Analyze(run.Statements);
        Assert.DoesNotContain(links, l => l.VariableName == "unused");
    }

    [Fact]
    public void Reassignment_updates_source()
    {
        const string src = """
            export class X {
                run(): number {
                    let a = 1;
                    a = 2;
                    return compute(a);
                }
            }
            """;
        var run = Extract(src, "run");
        var links = DataFlowAnalyzer.Analyze(run.Statements);
        Assert.Contains(links, l => l.VariableName == "a");
    }

    private static StatementNode Node(string id, StatementKind kind) => new()
    {
        Id = id,
        ParentSymbolId = "m",
        Kind = kind,
        StartLine = 1,
        Expression = id,
    };

    [Theory]
    [InlineData(StatementKind.ArrowFunction)]
    [InlineData(StatementKind.Closure)]
    public void Analyze_LambdaBody_IsNotFlattenedIntoTheEnclosingMember(StatementKind lambdaKind)
    {
        var write = Node("w", StatementKind.Assignment);
        write.AddReference(new SymbolReference("a", null, ReferenceKind.Write));
        var lambda = Node("lambda", lambdaKind);
        var inner = Node("inner", StatementKind.FunctionCall);
        inner.AddReference(new SymbolReference("a", null, ReferenceKind.Read));
        lambda.AddChild(inner);

        var links = DataFlowAnalyzer.Analyze([write, lambda]);

        Assert.Empty(links);
    }
}
