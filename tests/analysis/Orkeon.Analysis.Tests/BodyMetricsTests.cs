using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.TreeSitter;

namespace Orkeon.Analysis.Tests;

public class BodyMetricsTests
{
    private static RaggableNode Extract(string source, string name)
    {
        using var pool = new TreeSitterParserPool();
        var mapper = new UniversalSemanticMapper(new TypeScriptAdapter(), pool);
        var nodes = mapper.ExtractNodes("/r/a.ts", source);
        return nodes.First(n => n.Name == name);
    }

    [Fact]
    public void Linear_pipeline_cyclomatic_is_one()
    {
        var run = Extract(TestFixtures.LinearPipelineTs, "run");
        var body = run.Body;
        Assert.NotNull(body);
        Assert.Equal(1, body!.CyclomaticComplexity);
        Assert.Equal(0, body.BranchCount);
        Assert.Equal(0, body.LoopCount);
    }

    [Fact]
    public void Complex_method_reports_loops_and_trycatch_and_await()
    {
        var process = Extract(TestFixtures.ComplexMethodTs, "process");
        var body = process.Body;
        Assert.NotNull(body);
        Assert.True(body!.BranchCount >= 2, $"branch={body.BranchCount}");
        Assert.True(body.LoopCount >= 1, $"loops={body.LoopCount}");
        Assert.True(body.TryCatchCount >= 1);
        Assert.True(body.AwaitCount >= 1);
        Assert.True(body.CyclomaticComplexity >= 3);
    }

    [Fact]
    public void Broken_body_marks_ispartial()
    {
        var run = Extract(TestFixtures.BrokenBodyTs, "run");
        var body = run.Body;
        Assert.NotNull(body);
        Assert.True(body!.IsPartial);
    }

    [Fact]
    public void Empty_body_returns_null()
    {
        const string src = """
            export class X {
                noop(): void {}
            }
            """;
        var noop = Extract(src, "noop");
        Assert.Null(noop.Body);
    }

    [Fact]
    public void Closure_counts_arrow_function()
    {
        const string src = """
            export class X {
                run(): void {
                    const f = (x: number) => x + 1;
                    f(2);
                }
            }
            """;
        var run = Extract(src, "run");
        var body = run.Body;
        Assert.NotNull(body);
        Assert.True(body!.ClosureCount >= 1);
        Assert.True(body.CallCount >= 1);
    }

    [Fact]
    public void Actor_process_branch_and_trycatch()
    {
        var process = Extract(TestFixtures.ActorProcessTs, "_process");
        var body = process.Body;
        Assert.NotNull(body);
        Assert.True(body!.BranchCount >= 2);
        Assert.True(body.TryCatchCount >= 1);
        Assert.True(body.EarlyReturnCount >= 1);
    }
}
