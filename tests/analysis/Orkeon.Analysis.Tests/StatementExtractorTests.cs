using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.TreeSitter;

namespace Orkeon.Analysis.Tests;

public class StatementExtractorTests
{
    private static IReadOnlyList<RaggableNode> ExtractTs(string source)
    {
        using var pool = new TreeSitterParserPool();
        var mapper = new UniversalSemanticMapper(new TypeScriptAdapter(), pool);
        return mapper.ExtractNodes("/r/a.ts", source);
    }

    private static RaggableNode FindMethod(IReadOnlyList<RaggableNode> nodes, string name)
        => nodes.First(n => n.Name == name && n.Kind is UniversalNodeKind.Method or UniversalNodeKind.Function);

    [Fact]
    public void Linear_pipeline_extracts_statements_at_depth_zero()
    {
        var nodes = ExtractTs(TestFixtures.LinearPipelineTs);
        var run = FindMethod(nodes, "run");
        Assert.True(run.Statements.Count >= 4);
        Assert.All(run.Statements, s => Assert.Equal(0, s.Depth));
    }

    [Fact]
    public void Complex_method_nests_statements()
    {
        var nodes = ExtractTs(TestFixtures.ComplexMethodTs);
        var process = FindMethod(nodes, "process");
        Assert.Contains(process.Statements, s => s.Kind == StatementKind.If);
        Assert.Contains(process.Statements, s => s.Kind == StatementKind.TryCatch);
        var tryCatch = process.Statements.First(s => s.Kind == StatementKind.TryCatch);
        Assert.Contains(tryCatch.Children, c => c.Kind == StatementKind.ForOf);
    }

    [Fact]
    public void GetInitialSnapshot_has_6_statements()
    {
        var nodes = ExtractTs(TestFixtures.GetInitialSnapshotTs);
        var method = FindMethod(nodes, "getInitialSnapshot");
        var topLevel = method.Statements.Where(s => s.Depth == 0).ToList();
        Assert.Equal(6, topLevel.Count);
        var kinds = topLevel.Select(s => s.Kind).ToList();
        Assert.Equal(5, kinds.Count(k => k == StatementKind.VariableDecl));
        Assert.Contains(StatementKind.Return, kinds);
    }

    [Fact]
    public void GetInitialSnapshot_references_captured()
    {
        var nodes = ExtractTs(TestFixtures.GetInitialSnapshotTs);
        var method = FindMethod(nodes, "getInitialSnapshot");
        var machineDecl = method.Statements.First(s => s.Expression.Contains("const machine"));
        Assert.Contains(machineDecl.References, r => r.Kind == ReferenceKind.Write && r.Name == "machine");
    }

    [Fact]
    public void Broken_body_marks_partial()
    {
        var nodes = ExtractTs(TestFixtures.BrokenBodyTs);
        var method = FindMethod(nodes, "run");
        Assert.True(method.StatementsPartial);
        Assert.NotEmpty(method.Statements);
    }

    [Fact]
    public void Each_statement_has_unique_id()
    {
        var nodes = ExtractTs(TestFixtures.ComplexMethodTs);
        var process = FindMethod(nodes, "process");
        var ids = new HashSet<string>();
        void Walk(StatementNode s)
        {
            Assert.True(ids.Add(s.Id), $"Duplicate id: {s.Id}");
            foreach (var c in s.Children) Walk(c);
        }
        foreach (var s in process.Statements) Walk(s);
    }

    [Fact]
    public void Statement_id_references_parent_fqn()
    {
        var nodes = ExtractTs(TestFixtures.LinearPipelineTs);
        var run = FindMethod(nodes, "run");
        Assert.All(run.Statements, s => Assert.StartsWith(run.Fqn, s.Id));
        Assert.All(run.Statements, s => Assert.Equal(run.Fqn, s.ParentSymbolId));
    }
}
