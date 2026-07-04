using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.TreeSitter;

namespace Orkeon.Analysis.Tests;

public class NonRegressionTests
{
    [Fact]
    public void Empty_file_does_not_throw()
    {
        using var pool = new TreeSitterParserPool();
        var mapper = new UniversalSemanticMapper(new TypeScriptAdapter(), pool);
        var nodes = mapper.ExtractNodes("/r/empty.ts", TestFixtures.Empty);
        Assert.Single(nodes);
    }

    [Fact]
    public void Broken_syntax_extracts_module_with_partial_status()
    {
        using var pool = new TreeSitterParserPool();
        var mapper = new UniversalSemanticMapper(new TypeScriptAdapter(), pool);
        var nodes = mapper.ExtractNodes("/r/b.ts", TestFixtures.BrokenSyntax);
        Assert.Contains(nodes, n => n.Level == NodeLevel.L2_Module && n.ParseStatus != ParseStatus.Ok);
    }

    [Fact]
    public async Task Circular_import_does_not_loop()
    {
        var dir = Path.Combine(Path.GetTempPath(), "raggable-cycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var a = Path.Combine(dir, "a.ts");
            var b = Path.Combine(dir, "b.ts");
            await File.WriteAllTextAsync(a, "import { B } from './b';\nexport class A { run(): B { return new B(); } }\n", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(b, "import { A } from './a';\nexport class B { parent(): A { return new A(); } }\n", TestContext.Current.CancellationToken);

            using var pool = new TreeSitterParserPool();
            var adapter = new TypeScriptAdapter();
            var mapper = new UniversalSemanticMapper(adapter, pool);
            var builder = new DependencyGraphBuilder(adapter, pool);

            foreach (var path in new[] { a, b })
            {
                var src = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
                builder.RegisterFile(path, src, mapper.ExtractNodes(path, src));
            }

            var edges = await builder.BuildDependenciesAsync(new DefaultReferenceResolver(), CancellationToken.None);
            Assert.NotEmpty(edges);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Large_file_extracts_all_symbols()
    {
        using var pool = new TreeSitterParserPool();
        var mapper = new UniversalSemanticMapper(new TypeScriptAdapter(), pool);
        var source = TestFixtures.CreateLargeTs(symbolCount: 200);
        var nodes = mapper.ExtractNodes("/r/large.ts", source);
        var functions = nodes.Count(n => n.Kind == UniversalNodeKind.Function);
        Assert.Equal(200, functions);
    }
}
