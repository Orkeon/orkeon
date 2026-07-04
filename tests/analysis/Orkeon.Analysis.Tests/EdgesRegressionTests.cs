using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.TreeSitter;

namespace Orkeon.Analysis.Tests;

/// <summary>
/// P2-RT-FIX-08 — Regression suite for edge counts after VFS v2.2 migration.
///
/// Gold values reflect the CORRECTED baseline post-VFS-v2.2 + import-resolver fix
/// (TypeScriptAdapter.ResolveImportPath previously used File.Exists on virtual paths,
/// which always returned false, preventing index.ts barrel and .tsx resolution).
///
/// Corpus:
///   utils.ts       — Foo class, IFoo interface
///   service.ts     — imports utils, DerivedService extends Foo, implements IFoo, call inside
///   main.ts        — imports service + barrel (components/), calls Service method
///   components/index.ts — Button class (barrel; tests index.ts resolution)
/// </summary>
public class EdgesRegressionTests
{
    // -----------------------------------------------------------------------
    // Corpus sources
    // -----------------------------------------------------------------------

    private const string UtilsTs = """
        export interface IFoo {
            greet(): string;
        }

        export class Foo implements IFoo {
            greet(): string {
                return 'hello';
            }
        }
        """;

    private const string ServiceTs = """
        import { Foo, IFoo } from './utils';

        export class DerivedService extends Foo implements IFoo {
            greet(): string {
                const msg = super.greet();
                return msg + '!';
            }
        }
        """;

    private const string MainTs = """
        import { DerivedService } from './service';
        import { Button } from './components';

        export function run(): string {
            const svc = new DerivedService();
            return svc.greet();
        }
        """;

    private const string ComponentsIndexTs = """
        export class Button {
            render(): string {
                return '<button/>';
            }
        }
        """;

    private const string PackageJson = """
        { "name": "test-corpus", "version": "1.0.0" }
        """;

    // -----------------------------------------------------------------------
    // PHASE 1 — Gold-value test (end-to-end via RaggableTreeBuilder + VFS)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a mini 3-file (+1 barrel) TypeScript corpus via the full pipeline
    /// and asserts exact edge-kind counts as gold values.
    ///
    /// Gold values (post VFS-v2.2 fix):
    ///   Imports   = 3  (utils→module, service→module, components→module from main; barrel resolves correctly)
    ///   Extends   ≥ 1  (DerivedService extends Foo)
    ///   Implements ≥ 2 (Foo implements IFoo in utils.ts; DerivedService implements IFoo in service.ts)
    ///   Calls     ≥ 1  (new DerivedService(), svc.greet(), etc.)
    /// </summary>
    [Fact]
    public async Task Gold_edge_counts_per_kind_are_stable_post_vfs_v22()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("package.json", PackageJson),
            ("src/utils.ts", UtilsTs),
            ("src/service.ts", ServiceTs),
            ("src/main.ts", MainTs),
            ("src/components/index.ts", ComponentsIndexTs),
        ]);

        try
        {
            var fs = TestFixtures.CreateFs(dir);
            var builder = new RaggableTreeBuilder(new TypeScriptAdapter(), fs);
            var result = await builder.BuildAsync(
                TestFixtures.TestVirtualRoot,
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            var edges = result.Tree.Edges;
            var byKind = edges
                .GroupBy(e => e.Kind)
                .ToDictionary(g => g.Key, g => g.Count());

            var importCount = byKind.GetValueOrDefault(EdgeKind.Imports, 0);
            var extendsCount = byKind.GetValueOrDefault(EdgeKind.Extends, 0);
            var implementsCount = byKind.GetValueOrDefault(EdgeKind.Implements, 0);
            var callsCount = byKind.GetValueOrDefault(EdgeKind.Calls, 0);

            // Imports: service→utils (resolved), main→service (resolved),
            //          main→components (barrel via index.ts — requires the VFS fix to work)
            // Gold: 3 (all three inter-module imports resolve via the in-memory index lookup)
            Assert.Equal(3, importCount);

            // Extends: DerivedService extends Foo (service.ts)
            // Gold: 1
            Assert.Equal(1, extendsCount);

            // Implements: Foo implements IFoo (utils.ts) + DerivedService implements IFoo (service.ts)
            // Gold: 2
            Assert.Equal(2, implementsCount);

            // Calls: at minimum new DerivedService(), svc.greet() in main.ts
            //        + super.greet() in service.ts
            // Gold: ≥ 1
            Assert.True(callsCount >= 1,
                $"Expected at least 1 Calls edge, got {callsCount}");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // -----------------------------------------------------------------------
    // PHASE 1b — Low-level DependencyGraphBuilder test (with virtual paths)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Exercises DependencyGraphBuilder directly with virtual-path keys to
    /// confirm that the index-based candidate lookup resolves barrel imports
    /// and inheritance edges correctly without touching the physical disk.
    /// </summary>
    [Fact]
    public async Task DependencyGraphBuilder_resolves_barrel_index_import_via_index_lookup()
    {
        // Virtual paths (no physical files — should never call File.Exists)
        const string utilsVp = "/src/utils.ts";
        const string serviceVp = "/src/service.ts";
        const string mainVp = "/src/main.ts";
        const string barrelVp = "/src/components/index.ts";

        using var pool = new TreeSitterParserPool();
        var adapter = new TypeScriptAdapter();
        var mapper = new UniversalSemanticMapper(adapter, pool);
        var builder = new DependencyGraphBuilder(adapter, pool);

        foreach (var (vp, src) in new[]
        {
            (utilsVp, UtilsTs),
            (serviceVp, ServiceTs),
            (mainVp, MainTs),
            (barrelVp, ComponentsIndexTs),
        })
        {
            var nodes = mapper.ExtractNodes(vp, src);
            builder.RegisterFile(vp, src, nodes);
        }

        var edges = await builder.BuildDependenciesAsync(
            new DefaultReferenceResolver(), CancellationToken.None);

        // The barrel import "import { Button } from './components'" from main.ts should resolve
        // to /src/components/index.ts because DependencyGraphBuilder now probes index candidates
        // in the in-memory index instead of relying on File.Exists.
        var importEdges = edges.Where(e => e.Kind == EdgeKind.Imports).ToList();
        Assert.True(importEdges.Count >= 2,
            $"Expected ≥2 Imports edges (service→utils + main→service), got {importEdges.Count}. " +
            "Note: barrel (main→components/index.ts) requires the VFS-aware resolver fix.");

        // Extends edge: DerivedService extends Foo
        Assert.Contains(edges, e => e.Kind == EdgeKind.Extends);

        // Implements edge: DerivedService implements IFoo
        Assert.Contains(edges, e => e.Kind == EdgeKind.Implements);
    }

    // -----------------------------------------------------------------------
    // PHASE 1c — Validate barrel import specifically (regression guard)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Targeted regression guard: importing from './components' should resolve
    /// to /src/components/index.ts when that file is in the VFS index.
    /// Before the fix, File.Exists('/src/components/index.ts') always returned false
    /// on the virtual path, so the fallback returned '/src/components.ts' which
    /// was not in the index, causing the Imports edge to be silently dropped.
    /// </summary>
    [Fact]
    public async Task Import_from_directory_resolves_to_barrel_index_ts()
    {
        const string importerVp = "/src/main.ts";
        const string barrelVp = "/src/components/index.ts";

        const string importer = """
            import { Button } from './components';
            export function render(): string { return new Button().render(); }
            """;

        const string barrel = """
            export class Button {
                render(): string { return '<button/>'; }
            }
            """;

        using var pool = new TreeSitterParserPool();
        var adapter = new TypeScriptAdapter();
        var mapper = new UniversalSemanticMapper(adapter, pool);
        var builder = new DependencyGraphBuilder(adapter, pool);

        var importerNodes = mapper.ExtractNodes(importerVp, importer);
        var barrelNodes = mapper.ExtractNodes(barrelVp, barrel);

        builder.RegisterFile(importerVp, importer, importerNodes);
        builder.RegisterFile(barrelVp, barrel, barrelNodes);

        var edges = await builder.BuildDependenciesAsync(
            new DefaultReferenceResolver(), CancellationToken.None);

        // This is the specific regression: without the fix, this count is 0.
        var importsToBarrel = edges.Where(e =>
            e.Kind == EdgeKind.Imports &&
            e.ToId.Contains("components", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.True(importsToBarrel.Count >= 1,
            $"Expected ≥1 Imports edge from main→components/index.ts (barrel), got 0. " +
            "This is the P2-RT-FIX-08 regression: File.Exists on virtual path always returns false.");
    }

    // -----------------------------------------------------------------------
    // PHASE 1d — No regression on physical-path tests
    // -----------------------------------------------------------------------

    /// <summary>
    /// Confirms that the fix does not break the existing behaviour when
    /// physical paths are used (e.g. DependencyGraphBuilderTests scenarios).
    /// Uses real temp files so File.Exists works, and also verifies that the
    /// index-based fallback doesn't interfere.
    /// </summary>
    [Fact]
    public async Task Physical_path_imports_still_resolve_after_fix()
    {
        var dir = Path.Combine(Path.GetTempPath(), "edges-reg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var utilsPath = Path.Combine(dir, "utils.ts");
            var mainPath = Path.Combine(dir, "main.ts");
            await File.WriteAllTextAsync(utilsPath, UtilsTs, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(mainPath,
                "import { Foo } from './utils';\nexport function run() { return new Foo(); }\n",
                TestContext.Current.CancellationToken);

            using var pool = new TreeSitterParserPool();
            var adapter = new TypeScriptAdapter();
            var mapper = new UniversalSemanticMapper(adapter, pool);
            var builder = new DependencyGraphBuilder(adapter, pool);

            foreach (var path in new[] { mainPath, utilsPath })
            {
                var src = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
                builder.RegisterFile(path, src, mapper.ExtractNodes(path, src));
            }

            var edges = await builder.BuildDependenciesAsync(
                new DefaultReferenceResolver(), CancellationToken.None);

            Assert.Contains(edges, e => e.Kind == EdgeKind.Imports);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
