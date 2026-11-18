using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Analysis;

namespace Orkeon.Analysis.Tests;

/// <summary>Import edges of the languages whose import names a namespace, a package path or a module path (#252).</summary>
public class ImportEdgesTests
{
    private const string Root = TestFixtures.TestVirtualRoot;

    private static async Task<(BuildResult Result, string Dir)> BuildAsync(ILanguageAdapter adapter, params (string Path, string Content)[] files)
    {
        var dir = await TestFixtures.WriteDirectoryAsync(files);
        var builder = new RaggableTreeBuilder(adapter, TestFixtures.CreateFs(dir));
        var result = await builder.BuildAsync(Root, new IndexCodebaseRequest { RootPath = Root }, CancellationToken.None);
        return (result, dir);
    }

    private static string Module(string relative) => $"{Root}/{relative}#module@0";

    private static bool Imports(IReadOnlyList<RaggableEdge> edges, string from, string toId)
        => edges.Any(e => e.Kind == EdgeKind.Imports && e.FromId == Module(from) && e.ToId == toId);

    private static List<string> ImportTargets(IReadOnlyList<RaggableEdge> edges, string from)
        => [.. edges.Where(e => e.Kind == EdgeKind.Imports && e.FromId == Module(from)).Select(e => e.ToId).Order(StringComparer.Ordinal)];

    private const string CSharpAlpha = "namespace Demo.Core;\npublic class Alpha { }\n";
    private const string CSharpBeta = "namespace Demo.Core\n{\n    public static class Beta { public static int Two() => 2; }\n}\n";

    [Fact]
    public async Task CSharp_using_draws_an_edge_only_to_the_files_declaring_a_type_the_importer_names()
    {
        var (result, dir) = await BuildAsync(new CSharpAdapter(),
            ("Core/Alpha.cs", CSharpAlpha),
            ("Core/Beta.cs", CSharpBeta),
            ("App/Program.cs", "using Demo.Core;\nnamespace Demo.App;\npublic class Program { public Alpha Make() => new Alpha(); }\n"));
        try
        {
            var edges = result.Tree.Edges;
            Assert.True(Imports(edges, "App/Program.cs", Module("Core/Alpha.cs")));
            Assert.False(Imports(edges, "App/Program.cs", Module("Core/Beta.cs")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task CSharp_using_static_alias_and_global_using_name_their_files()
    {
        var (result, dir) = await BuildAsync(new CSharpAdapter(),
            ("Core/Alpha.cs", CSharpAlpha),
            ("Core/Beta.cs", CSharpBeta),
            ("Core/Gamma.cs", "namespace Demo.Core.Inner { public enum Gamma { A } }\n"),
            ("App/Program.cs", """
                global using Demo.Core.Inner;
                using static Demo.Core.Beta;
                using A = Demo.Core.Alpha;
                namespace Demo.App
                {
                    public class Program { public int Run() => Two() + (int)Gamma.A; }
                }
                """));
        try
        {
            Assert.Equal(
                [Module("Core/Alpha.cs"), Module("Core/Beta.cs"), Module("Core/Gamma.cs")],
                ImportTargets(result.Tree.Edges, "App/Program.cs"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task CSharp_using_of_a_namespace_outside_the_tree_goes_to_an_external_node()
    {
        var (result, dir) = await BuildAsync(new CSharpAdapter(),
            ("App/Program.cs", "using System.Text;\nusing Ext = Newtonsoft.Json.JsonConvert;\nnamespace Demo.App;\npublic class Program { }\n"));
        try
        {
            Assert.Equal(
                ["ext::dotnet::Newtonsoft.Json.JsonConvert", "ext::dotnet::System.Text"],
                ImportTargets(result.Tree.Edges, "App/Program.cs"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task CSharp_package_summary_lists_the_package_its_usings_reach()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("Lib/Lib.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />"),
            ("Lib/Alpha.cs", CSharpAlpha),
            ("App/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />"),
            ("App/Program.cs", "using System;\nusing Demo.Core;\nnamespace Demo.App;\npublic class Program { public Alpha Make() => new Alpha(); }\n"),
        ]);
        try
        {
            var builder = new RaggableTreeBuilder(new CSharpAdapter(), TestFixtures.CreateFs(dir));
            var result = await builder.BuildAsync(Root, new IndexCodebaseRequest { RootPath = Root }, CancellationToken.None);
            var store = new InMemoryRaggableStore(result.Tree, new FakeFileSystemService());
            var packages = result.Tree.Nodes.Where(n => n.Level == NodeLevel.L1_Package).ToDictionary(n => n.Name);
            using var tool = new PackageSummaryTool(store);

            var app = await tool.ExecuteTypedForTest(new PackageSummaryRequest { Fqn = packages["App"].Fqn }, CancellationToken.None);
            var lib = await tool.ExecuteTypedForTest(new PackageSummaryRequest { Fqn = packages["Lib"].Fqn }, CancellationToken.None);

            Assert.Equal([packages["Lib"].Fqn], app.Dependencies);
            Assert.Empty(lib.Dependencies);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task CSharp_incremental_reindex_rebuilds_the_using_edges_of_the_changed_file()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("Core/Alpha.cs", CSharpAlpha),
            ("Core/Beta.cs", CSharpBeta),
            ("App/Program.cs", "using Demo.Core;\nnamespace Demo.App;\npublic class Program { public Alpha Make() => new Alpha(); }\n"),
        ]);
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            var initial = await new RaggableTreeBuilder(new CSharpAdapter(), fs)
                .BuildAsync(Root, new IndexCodebaseRequest { RootPath = Root }, CancellationToken.None);
            await File.WriteAllTextAsync(
                Path.Combine(dir, "App", "Program.cs"),
                "using Demo.Core;\nnamespace Demo.App;\npublic class Program { public int Run() => Beta.Two(); }\n",
                TestContext.Current.CancellationToken);

            var reindexed = await new IncrementalReindexEngine(new CSharpAdapter(), fs).ReindexAsync(
                initial.Tree, Root, [$"{Root}/App/Program.cs"],
                new IndexCodebaseRequest { RootPath = Root }, CancellationToken.None);

            Assert.Equal([Module("Core/Beta.cs")], ImportTargets(reindexed.Tree.Edges, "App/Program.cs"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private const string GoMod = "// app\nmodule example.com/app\n\ngo 1.22\n";

    [Fact]
    public async Task Go_import_under_the_module_path_reaches_the_files_of_its_directory()
    {
        var (result, dir) = await BuildAsync(new GoAdapter(),
            ("go.mod", GoMod),
            ("util/strings.go", "package util\n\nfunc Upper(s string) string { return s }\n"),
            ("util/numbers.go", "package util\n\nfunc Two() int { return 2 }\n"),
            ("util/strings_test.go", "package util\n\nfunc helper() {}\n"),
            ("main.go", "package main\n\nimport (\n\t\"fmt\"\n\tu \"example.com/app/util\"\n)\n\nfunc main() { fmt.Println(u.Upper(\"a\")) }\n"));
        try
        {
            Assert.Equal(
                [Module("util/numbers.go"), Module("util/strings.go"), "ext::go::fmt"],
                ImportTargets(result.Tree.Edges, "main.go"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Go_incremental_reindex_keeps_resolving_the_module_path()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("go.mod", GoMod),
            ("util/strings.go", "package util\n\nfunc Upper(s string) string { return s }\n"),
            ("main.go", "package main\n\nfunc main() {}\n"),
        ]);
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            var initial = await new RaggableTreeBuilder(new GoAdapter(), fs)
                .BuildAsync(Root, new IndexCodebaseRequest { RootPath = Root }, CancellationToken.None);
            await File.WriteAllTextAsync(
                Path.Combine(dir, "main.go"),
                "package main\n\nimport \"example.com/app/util\"\n\nfunc main() { util.Upper(\"a\") }\n",
                TestContext.Current.CancellationToken);

            var reindexed = await new IncrementalReindexEngine(new GoAdapter(), fs).ReindexAsync(
                initial.Tree, Root, [$"{Root}/main.go"],
                new IndexCodebaseRequest { RootPath = Root }, CancellationToken.None);

            Assert.Equal([Module("util/strings.go")], ImportTargets(reindexed.Tree.Edges, "main.go"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Rust_crate_super_and_child_module_paths_reach_module_files_and_std_is_external()
    {
        var (result, dir) = await BuildAsync(new RustAdapter(),
            ("Cargo.toml", "[package]\nname = \"app\"\n"),
            ("src/main.rs", """
                mod config;
                mod net;
                use crate::net::client::{Client, connect as open};
                use std::collections::HashMap;
                use config::Settings;
                fn main() {}
                """),
            ("src/config.rs", "pub struct Settings;\n"),
            ("src/net/mod.rs", "pub mod client;\npub struct Helper;\n"),
            ("src/net/client.rs", "use super::Helper;\nuse self::inner::*;\npub struct Client;\npub fn connect() {}\n"));
        try
        {
            var edges = result.Tree.Edges;
            Assert.Equal(
                [Module("src/config.rs"), Module("src/net/client.rs"), "ext::cargo::std"],
                ImportTargets(edges, "src/main.rs"));
            Assert.Equal([Module("src/net/mod.rs")], ImportTargets(edges, "src/net/client.rs"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    [Trait("BR", "R-07")]
    public async Task ShouldNotDrawImportEdge_WhenUseSuperIsInsideInlineModule()
    {
        var (result, dir) = await BuildAsync(new RustAdapter(),
            ("Cargo.toml", "[package]\nname = \"app\"\n"),
            ("src/main.rs", "mod net;\nfn main() {}\n"),
            ("src/net/mod.rs", "pub mod client;\npub struct Helper;\n"),
            ("src/net/client.rs", "pub struct Client;\n#[cfg(test)]\nmod tests {\n    use super::*;\n}\n"));
        try
        {
            Assert.DoesNotContain(Module("src/net/mod.rs"), ImportTargets(result.Tree.Edges, "src/net/client.rs"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    [Trait("BR", "R-35")]
    public async Task ShouldDrawImportEdge_WhenNewGoFileJoinsImportedPackageAfterRefresh()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("go.mod", GoMod),
            ("util/strings.go", "package util\n\nfunc Upper(s string) string { return s }\n"),
            ("main.go", "package main\n\nimport \"example.com/app/util\"\n\nfunc main() { util.Upper(\"a\") }\n"),
        ]);
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            var initial = await new RaggableTreeBuilder(new GoAdapter(), fs)
                .BuildAsync(Root, new IndexCodebaseRequest { RootPath = Root }, CancellationToken.None);
            await File.WriteAllTextAsync(
                Path.Combine(dir, "util", "numbers.go"),
                "package util\n\nfunc Two() int { return 2 }\n",
                TestContext.Current.CancellationToken);

            var reindexed = await new IncrementalReindexEngine(new GoAdapter(), fs).ReindexAsync(
                initial.Tree, Root, [$"{Root}/util/numbers.go"],
                new IndexCodebaseRequest { RootPath = Root }, CancellationToken.None);

            Assert.Equal(
                [Module("util/numbers.go"), Module("util/strings.go")],
                ImportTargets(reindexed.Tree.Edges, "main.go"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    [Trait("BR", "R-35")]
    public async Task ShouldNotReparseGoImporters_WhenImportedPackageFileIsOnlyEdited()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("go.mod", GoMod),
            ("util/strings.go", "package util\n\nfunc Upper(s string) string { return s }\n"),
            ("main.go", "package main\n\nimport \"example.com/app/util\"\n\nfunc main() { util.Upper(\"a\") }\n"),
        ]);
        try
        {
            var initial = await new RaggableTreeBuilder(new GoAdapter(), TestFixtures.CreateFs(dir))
                .BuildAsync(Root, new IndexCodebaseRequest { RootPath = Root }, CancellationToken.None);
            await File.WriteAllTextAsync(
                Path.Combine(dir, "util", "strings.go"),
                "package util\n\nfunc Upper(s string) string { return s + \"!\" }\n",
                TestContext.Current.CancellationToken);
            var counting = new Doubles.FakeCountingFileSystemService(TestFixtures.CreateFs(dir));

            var reindexed = await new IncrementalReindexEngine(new GoAdapter(), counting).ReindexAsync(
                initial.Tree, Root, [$"{Root}/util/strings.go"],
                new IndexCodebaseRequest { RootPath = Root }, CancellationToken.None);

            Assert.Contains($"{Root}/util/strings.go", counting.Reads);
            Assert.DoesNotContain($"{Root}/main.go", counting.Reads);
            Assert.Equal([Module("util/strings.go")], ImportTargets(reindexed.Tree.Edges, "main.go"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static readonly (string Path, string Content)[] PythonTree =
    [
        ("trading/bilan.py", "LIMITES = {\"a\": 1}\ndef poche(x): return x\n"),
        ("trading/backtest.py", "import bilan\nfrom bilan import LIMITES\n"),
        ("trading/matin.py", "import backtest\n"),
        ("pkg/__init__.py", "VERSION = 1\n"),
        ("pkg/sub/__init__.py", ""),
        ("pkg/sub/util.py", "def g(): return 2\n"),
        ("pkg/util.py", "def g(): return 2\n"),
        ("pkg/main.py", "from pkg.sub import util\nfrom . import util as u2\n"),
    ];

    [Fact]
    [Trait("BR", "#406")]
    public async Task ShouldDrawPythonImportEdges_WhenAnImportNamesAFileOfTheProject()
    {
        var (result, dir) = await BuildAsync(new PythonAdapter(), PythonTree);
        try
        {
            var edges = result.Tree.Edges;
            // An absolute import of a package of the project, and a relative one.
            Assert.Equal(
                [Module("pkg/sub/util.py"), Module("pkg/util.py")],
                ImportTargets(edges, "pkg/main.py"));
            // A same-folder import, as Python resolves it for a script run from that folder.
            Assert.Equal([Module("trading/bilan.py")], ImportTargets(edges, "trading/backtest.py"));
            Assert.Equal([Module("trading/backtest.py")], ImportTargets(edges, "trading/matin.py"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    [Trait("BR", "#406")]
    public async Task ShouldReachThePackageOrTheSubmodule_WhenAPythonFromImportNamesEither()
    {
        var (result, dir) = await BuildAsync(new PythonAdapter(),
            ("pkg/__init__.py", "VERSION = 1\n"),
            ("pkg/core.py", "def run(): return 1\n"),
            ("pkg/sub/__init__.py", ""),
            ("pkg/sub/deep.py", "from .. import core\nfrom ..core import run\nfrom .nowhere import x\n"),
            ("pkg/sub/names.py", "from . import LEVEL\n"),
            ("app/names.py", "from pkg import VERSION, core\n"),
            ("app/star.py", "from pkg.core import *\n"),
            ("app/dotted.py", "import pkg.sub.deep as d, pkg.core\n"),
            ("app/lazy.py", "def late():\n    import pkg.core\n    return pkg.core.run()\n"));
        try
        {
            var edges = result.Tree.Edges;
            // Two dots: one package up. A relative import that reaches no file draws nothing.
            Assert.Equal([Module("pkg/core.py")], ImportTargets(edges, "pkg/sub/deep.py"));
            // A name imported from the package that is no submodule is the package's own.
            Assert.Equal([Module("pkg/sub/__init__.py")], ImportTargets(edges, "pkg/sub/names.py"));
            // A name that is no submodule comes from the package itself, a submodule from its file.
            Assert.Equal([Module("pkg/__init__.py"), Module("pkg/core.py")], ImportTargets(edges, "app/names.py"));
            Assert.Equal([Module("pkg/core.py")], ImportTargets(edges, "app/star.py"));
            Assert.Equal([Module("pkg/core.py"), Module("pkg/sub/deep.py")], ImportTargets(edges, "app/dotted.py"));
            // An import inside a function is an import of the file.
            Assert.Equal([Module("pkg/core.py")], ImportTargets(edges, "app/lazy.py"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    [Trait("BR", "#406")]
    public async Task ShouldSendAPythonImportToAnExternalNode_WhenNoFileOfTheProjectHoldsIt()
    {
        var (result, dir) = await BuildAsync(new PythonAdapter(),
            ("app/main.py", "from __future__ import annotations\nimport os.path\nfrom numpy.linalg import norm\nimport local\n"),
            ("app/local.py", "X = 1\n"));
        try
        {
            // The first segment names the distribution; __future__ is a directive, not a module.
            Assert.Equal(
                [Module("app/local.py"), "ext::pypi::numpy", "ext::pypi::os"],
                ImportTargets(result.Tree.Edges, "app/main.py"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    [Trait("BR", "#406")]
    public async Task ShouldSearchAbovePackagesFirst_WhenThePythonImporterIsInsideAPackage()
    {
        var (result, dir) = await BuildAsync(new PythonAdapter(),
            ("utils.py", "TOP = 1\n"),
            ("app/__init__.py", ""),
            ("app/utils.py", "INNER = 1\n"),
            ("app/packaged.py", "import utils\n"),
            ("app/only_here.py", "import packaged\n"),
            ("scripts/utils.py", "LOCAL = 1\n"),
            ("scripts/run.py", "import utils\n"),
            ("root_script.py", "import utils\nimport app.utils\n"));
        try
        {
            var edges = result.Tree.Edges;
            // Inside a package the sibling is not on the path: the module above the package wins.
            Assert.Equal([Module("utils.py")], ImportTargets(edges, "app/packaged.py"));
            // ... and the sibling is the fallback, for a file run as a script.
            Assert.Equal([Module("app/packaged.py")], ImportTargets(edges, "app/only_here.py"));
            // Outside a package the file is a script: its own folder comes first.
            Assert.Equal([Module("scripts/utils.py")], ImportTargets(edges, "scripts/run.py"));
            // A file directly under the indexed root.
            Assert.Equal([Module("app/utils.py"), Module("utils.py")], ImportTargets(edges, "root_script.py"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    [Trait("BR", "#406")]
    public async Task ShouldFallBackToTheLeadingModuleOrTheLibrary_WhenAPythonImportIsNotAFileAsWritten()
    {
        var (result, dir) = await BuildAsync(new PythonAdapter(),
            ("lib/conf.py", "class settings: DEBUG = 1\n"),
            ("lib/attr.py", "import conf.settings\n"),
            ("lib/redis.py", "import redis\n"),
            ("lib/deep.py", "from ....nowhere import x\nimport lib_missing.sub\n"));
        try
        {
            var edges = result.Tree.Edges;
            // `settings` is an attribute of conf.py, not a file: the import reaches conf.py.
            Assert.Equal([Module("lib/conf.py")], ImportTargets(edges, "lib/attr.py"));
            // A wrapper named like the library it imports does not import itself.
            Assert.Equal(["ext::pypi::redis"], ImportTargets(edges, "lib/redis.py"));
            // More dots than folders draws nothing; a name nothing holds is external.
            Assert.Equal(["ext::pypi::lib_missing"], ImportTargets(edges, "lib/deep.py"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }


    [Fact]
    [Trait("BR", "#406")]
    public async Task ShouldReResolvePythonImporters_WhenTheFileTheyNameJoinsTheTree()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("pkg/__init__.py", "VERSION = 1\n"),
            ("app/absolute.py", "import pkg.late\n"),
            ("app/named.py", "from pkg import late\n"),
            ("app/other.py", "import os\n"),
        ]);
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            var initial = await new RaggableTreeBuilder(new PythonAdapter(), fs)
                .BuildAsync(Root, new IndexCodebaseRequest { RootPath = Root }, CancellationToken.None);
            // Before the file exists, both reach the package: the leading part of the dotted path, and the
            // module the name is imported from.
            Assert.Equal([Module("pkg/__init__.py")], ImportTargets(initial.Tree.Edges, "app/absolute.py"));
            Assert.Equal([Module("pkg/__init__.py")], ImportTargets(initial.Tree.Edges, "app/named.py"));
            await File.WriteAllTextAsync(
                Path.Combine(dir, "pkg", "late.py"), "def f(): return 1\n", TestContext.Current.CancellationToken);

            var reindexed = await new IncrementalReindexEngine(new PythonAdapter(), fs).ReindexAsync(
                initial.Tree, Root, [$"{Root}/pkg/late.py"],
                new IndexCodebaseRequest { RootPath = Root }, CancellationToken.None);

            Assert.Equal([Module("pkg/late.py")], ImportTargets(reindexed.Tree.Edges, "app/absolute.py"));
            Assert.Equal([Module("pkg/late.py")], ImportTargets(reindexed.Tree.Edges, "app/named.py"));
            // Only the two importers and the new file were parsed again.
            Assert.Equal(3, reindexed.FilesParsed);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    [Trait("BR", "#406")]
    public async Task ShouldReResolvePythonImporters_WhenTheModuleTheyTookForALibraryJoinsTheTree()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("app/run.py", "import helper\n"),
            ("app/other.py", "import os\n"),
        ]);
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            var initial = await new RaggableTreeBuilder(new PythonAdapter(), fs)
                .BuildAsync(Root, new IndexCodebaseRequest { RootPath = Root }, CancellationToken.None);
            // Before the file exists, nothing in the tree holds the name: the import is a library.
            Assert.Equal(["ext::pypi::helper"], ImportTargets(initial.Tree.Edges, "app/run.py"));
            await File.WriteAllTextAsync(
                Path.Combine(dir, "app", "helper.py"), "def f(): return 1\n", TestContext.Current.CancellationToken);

            var reindexed = await new IncrementalReindexEngine(new PythonAdapter(), fs).ReindexAsync(
                initial.Tree, Root, [$"{Root}/app/helper.py"],
                new IndexCodebaseRequest { RootPath = Root }, CancellationToken.None);

            Assert.Equal([Module("app/helper.py")], ImportTargets(reindexed.Tree.Edges, "app/run.py"));
            Assert.Equal(ImportTargets(initial.Tree.Edges, "app/other.py"), ImportTargets(reindexed.Tree.Edges, "app/other.py"));
            // Only the importer and the new file were parsed again.
            Assert.Equal(2, reindexed.FilesParsed);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    [Trait("BR", "#406")]
    public async Task ShouldRebuildPythonImportEdges_WhenTheImporterChanges()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("lib/a.py", "A = 1\n"),
            ("lib/b.py", "B = 2\n"),
            ("lib/main.py", "import a\n"),
        ]);
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            var initial = await new RaggableTreeBuilder(new PythonAdapter(), fs)
                .BuildAsync(Root, new IndexCodebaseRequest { RootPath = Root }, CancellationToken.None);
            await File.WriteAllTextAsync(
                Path.Combine(dir, "lib", "main.py"), "from . import b\n", TestContext.Current.CancellationToken);

            var reindexed = await new IncrementalReindexEngine(new PythonAdapter(), fs).ReindexAsync(
                initial.Tree, Root, [$"{Root}/lib/main.py"],
                new IndexCodebaseRequest { RootPath = Root }, CancellationToken.None);

            Assert.Equal([Module("lib/b.py")], ImportTargets(reindexed.Tree.Edges, "lib/main.py"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }


    [Theory]
    [InlineData("crate::a::B", "crate::a::B")]
    [InlineData("crate::a::{b::C, d::{self, E as F}}", "crate::a::b::C|crate::a::d::self|crate::a::d::E")]
    [InlineData("std::io as sio", "std::io")]
    public void Rust_use_lists_expand_to_one_path_each(string argument, string expected)
    {
        Assert.Equal(expected.Split('|'), DependencyGraphBuilder.ExpandRustUse(argument));
    }

    [Theory]
    [InlineData("var u = \"http://x\"; Alpha a;", "Alpha", true)]
    [InlineData("var u = @\"a\"\"b\"; Alpha a;", "Alpha", true)]
    [InlineData("var s = \"Beta\";", "Beta", true)]
    [InlineData("// Alpha\nvar x = 1;", "Alpha", false)]
    [InlineData("/* Alpha */ var x = 1;", "Alpha", false)]
    public void CSharp_identifiers_skip_comments_but_not_a_comment_marker_inside_a_string(string source, string identifier, bool expected)
    {
        Assert.Equal(expected, DependencyGraphBuilder.CSharpIdentifiers(source).Contains(identifier));
    }

    [Theory]
    [InlineData("using System.Text;", "System.Text")]
    [InlineData("global using static global::Demo.Core.Beta;", "Demo.Core.Beta")]
    [InlineData("using L = System.Collections.Generic.List<int>;", "System.Collections.Generic.List")]
    public void CSharp_using_name_drops_keywords_alias_and_generic_arguments(string directive, string expected)
    {
        Assert.Equal(expected, DependencyGraphBuilder.CSharpUsingName(directive));
    }
}
