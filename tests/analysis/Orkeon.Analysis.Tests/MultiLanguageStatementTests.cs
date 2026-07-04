using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.TreeSitter;

namespace Orkeon.Analysis.Tests;

public class MultiLanguageStatementTests
{
    private static IReadOnlyList<RaggableNode> Extract(ILanguageAdapter adapter, string path, string source)
    {
        using var pool = new TreeSitterParserPool();
        var mapper = new UniversalSemanticMapper(adapter, pool);
        return mapper.ExtractNodes(path, source);
    }

    [Fact]
    public void Python_method_extracts_if_and_for()
    {
        const string src = """
            class Service:
                def process(self, items):
                    count = 0
                    if items:
                        for item in items:
                            count = count + 1
                    return count
            """;
        var nodes = Extract(new Orkeon.Analysis.Adapters.PythonAdapter(), "/r/a.py", src);
        var process = nodes.First(n => n.Name == "process");
        Assert.Contains(process.Statements, s => s.Kind == StatementKind.If);
        Assert.Contains(process.Statements, s => s.Kind == StatementKind.Return);
        var body = process.Body;
        Assert.NotNull(body);
        Assert.True(body!.LoopCount >= 1);
    }

    [Fact]
    public void Csharp_method_extracts_try_catch_and_foreach()
    {
        const string src = """
            namespace Example;
            public class Svc
            {
                public int Run(int[] items)
                {
                    var total = 0;
                    try
                    {
                        foreach (var i in items)
                        {
                            total = total + i;
                        }
                    }
                    catch (System.Exception)
                    {
                        total = -1;
                    }
                    return total;
                }
            }
            """;
        var nodes = Extract(new Orkeon.Analysis.Adapters.CSharpAdapter(), "/r/a.cs", src);
        var run = nodes.First(n => n.Name == "Run");
        Assert.Contains(run.Statements, s => s.Kind == StatementKind.TryCatch);
        var body = run.Body;
        Assert.NotNull(body);
        Assert.True(body!.LoopCount >= 1);
        Assert.True(body.TryCatchCount >= 1);
    }

    [Fact]
    public void Go_function_extracts_if_and_for()
    {
        const string src = """
            package main

            func Run(items []int) int {
                total := 0
                for _, v := range items {
                    total = total + v
                }
                if total > 100 {
                    return 100
                }
                return total
            }
            """;
        var nodes = Extract(new Orkeon.Analysis.Adapters.GoAdapter(), "/r/a.go", src);
        var run = nodes.First(n => n.Name == "Run");
        Assert.Contains(run.Statements, s => s.Kind == StatementKind.For);
        Assert.Contains(run.Statements, s => s.Kind == StatementKind.If);
    }

    [Fact]
    public void Rust_function_extracts_match_and_for()
    {
        const string src = """
            pub fn run(items: &[i32]) -> i32 {
                let mut total = 0;
                for v in items {
                    total += v;
                }
                match total {
                    0 => 0,
                    _ => total,
                }
            }
            """;
        var nodes = Extract(new Orkeon.Analysis.Adapters.RustAdapter(), "/r/a.rs", src);
        var run = nodes.First(n => n.Name == "run");
        Assert.Contains(run.Statements, s => s.Kind == StatementKind.ForOf);
        Assert.Contains(run.Statements, s => s.Kind == StatementKind.Switch);
    }
}
