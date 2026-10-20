using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Orkeon.Scripting.Tests.Architecture;

/// <summary>
/// CH-04: no test waits on a Jint engine without a bound. Every engine a test builds takes its
/// <c>PromiseTimeout</c> from <c>HangGuards.EnginePromise</c>, every engine the product builds
/// (<c>JsEngineFactory.Create()</c>, <c>ScriptHost</c>) gets it through the factory's internal setting, assigned
/// once per test project, and a symbol Roslyn cannot
/// resolve is a violation, never a pass. The sweep compiles the test sources of the four projects
/// that drive scripts. Rule 2 (<c>EvaluateAsync</c>, <c>UnwrapIfPromiseAsync</c>): the receiver's type must resolve
/// and be a Jint type; the call is accepted when no call, member or local initialiser behind the receiver yields a
/// Jint engine from a non-test assembly other than Jint, <c>JsEngineFactory.Create()</c> and
/// <c>ScriptHost</c> (every engine the projects build is covered by rules 1 and 3), a violation otherwise.
/// </summary>
public sealed class TestEngineHangGuardTests
{
    private static readonly string[] Projects =
    [
        "tests/scripting/Orkeon.Scripting.Tests",
        "tests/cli/Orkeon.Cli.Commands.Scripting.Tests",
        "tests/scripting/Orkeon.Scripting.Cli.Tests",
        "tests/apps/Orkeon.ConsoleApp.Tests",
    ];

    private static bool HasGuard(string arguments)
    {
        var compact = string.Concat(arguments.Where(c => !char.IsWhiteSpace(c)));
        return compact.Contains("PromiseTimeout=HangGuards.EnginePromise", StringComparison.Ordinal)
               || compact.Contains("PromiseTimeout=Orkeon.Tests.Shared.Timing.HangGuards.EnginePromise", StringComparison.Ordinal);
    }

    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    [Fact]
    [Trait("BR", "CH-04")]
    public void Tests_await_engines_only_under_the_named_hang_guard()
    {
        var root = RepoRoot();
        var violations = new List<(string Path, int Line, string Reason)>();
        var scans = new List<(string Project, ProjectScan Scan)>();

        foreach (var project in Projects)
        {
            var compilation = BuildCompilation(root, project);
            var scan = new ProjectScan();
            scans.Add((project, scan));
            foreach (var tree in compilation.SyntaxTrees.Where(t => !t.FilePath.EndsWith("ImplicitUsings.g.cs", StringComparison.Ordinal)))
                Analyse(compilation.GetSemanticModel(tree), tree, violations, scan);
        }

        foreach (var (project, scan) in scans)
        {
            var sites = scan.CreateSites.Count + scan.ScriptHostSites.Count;
            if (sites == 0 || scan.Assigned) continue;
            violations.Add((project, 1, $"{scan.CreateSites.Count} JsEngineFactory.Create() and {scan.ScriptHostSites.Count} ScriptHost site(s) and no `JsEngineFactory.PromiseTimeout = HangGuards.EnginePromise` anywhere in the project"));
            foreach (var site in scan.CreateSites.Concat(scan.ScriptHostSites))
                violations.Add((site.Path, site.Line, "engine built by the product without the project-wide `JsEngineFactory.PromiseTimeout = HangGuards.EnginePromise`"));
        }

        var lines = violations
            .OrderBy(v => v.Path, StringComparer.Ordinal).ThenBy(v => v.Line)
            .Select(v => $"{v.Path}:{v.Line} — {v.Reason}")
            .ToList();
        var files = violations.Select(v => v.Path).Distinct(StringComparer.Ordinal).Count();
        Assert.True(
            lines.Count == 0,
            $"{lines.Count} violation(s) in {files} file(s):\n" + string.Join("\n", lines)
            + "\nPer project (Create() sites / ScriptHost sites / setting assigned):\n"
            + string.Join("\n", scans.Select(x => $"{x.Project}: {x.Scan.CreateSites.Count} / {x.Scan.ScriptHostSites.Count} / {x.Scan.Assigned}")));
    }

    private static void Analyse(SemanticModel model, SyntaxTree tree, List<(string, int, string)> violations, ProjectScan scan)
    {
        var path = tree.FilePath;
        var nodes = tree.GetRoot().DescendantNodes().ToList();

        void Add(SyntaxNode node, string reason)
            => violations.Add((path, node.GetLocation().GetLineSpan().StartLinePosition.Line + 1, reason));

        void Site(List<(string Path, int Line)> list, SyntaxNode node)
            => list.Add((path, node.GetLocation().GetLineSpan().StartLinePosition.Line + 1));

        // Rule 1 and 4: every engine construction.
        foreach (var node in nodes)
        {
            if (node is AssignmentExpressionSyntax assignment && IsFactorySetting(model, assignment) && InModuleInitializer(model, assignment))
                scan.Assigned = true;
            if (node is BaseObjectCreationExpressionSyntax creation)
            {
                var type = model.GetTypeInfo(creation).Type;
                var unresolved = type is null || type.TypeKind == TypeKind.Error;
                if (unresolved)
                {
                    var written = (creation as ObjectCreationExpressionSyntax)?.Type.ToString();
                    if (written is "Engine" or "Jint.Engine")
                        Add(creation, "unresolved symbol on `new " + written + "(`");
                    continue;
                }
                if (IsNamed(type!, "Jint", "Engine"))
                {
                    var arguments = creation.ArgumentList?.ToString() ?? string.Empty;
                    if (!HasGuard(arguments)) Add(creation, "Jint.Engine built without `PromiseTimeout = HangGuards.EnginePromise`");
                }
                else if (IsNamed(type!, "Orkeon.Scripting", "ScriptHost"))
                {
                    Site(scan.ScriptHostSites, creation);
                }
            }
            else if (node is InvocationExpressionSyntax invocation && InvokedName(invocation) == "Create")
            {
                var method = ResolveMethod(model, invocation);
                if (method is null)
                {
                    var receiver = (invocation.Expression as MemberAccessExpressionSyntax)?.Expression;
                    var receiverType = receiver is null ? null : model.GetTypeInfo(receiver).Type;
                    if (receiverType is null || receiverType.TypeKind == TypeKind.Error)
                        Add(invocation, "unresolved symbol on `" + invocation.Expression + "(`");
                    continue;
                }
                if (!IsNamed(method.ContainingType, "Orkeon.Scripting", "JsEngineFactory")) continue;
                Site(scan.CreateSites, invocation);
            }
        }

        // Rule 2: every async entry into an engine.
        foreach (var invocation in nodes.OfType<InvocationExpressionSyntax>())
        {
            var invoked = InvokedName(invocation);
            if (invoked is not ("EvaluateAsync" or "UnwrapIfPromiseAsync")) continue;
            var method = ResolveMethod(model, invocation);
            var receiver = (invocation.Expression as MemberAccessExpressionSyntax)?.Expression;
            if (method is null)
            {
                var receiverType = receiver is null ? null : model.GetTypeInfo(receiver).Type;
                if (receiverType is null || receiverType.TypeKind == TypeKind.Error)
                    Add(invocation, "unresolved receiver on `" + invoked + "`");
                continue;
            }
            var owner = method.ReducedFrom?.ContainingType ?? method.ContainingType;
            if (!IsJint(owner)) continue;
            if (receiver is null) { Add(invocation, "`" + invoked + "` on an engine of unknown origin"); continue; }
            var receiverKind = model.GetTypeInfo(receiver).Type;
            if (receiverKind is null || receiverKind.TypeKind == TypeKind.Error)
            {
                Add(invocation, "unresolved receiver type on `" + invoked + "`");
                continue;
            }
            if (!IsNamed(receiverKind, "Jint", "Engine") && !IsNamed(receiverKind, "Jint.Native", "JsValue") && !IsJintType(receiverKind))
            {
                Add(invocation, "`" + invoked + "` on a receiver `" + receiver + "` that is neither a Jint engine nor a Jint value");
                continue;
            }
            if (ForeignOrigin(model, receiver, 0) is { } foreign)
                Add(invocation, "`" + invoked + "` on `" + receiver + "` coming from `" + foreign + "`, not from an engine the test projects build");
        }
    }

    private static bool IsJintType(ITypeSymbol type) => type is INamedTypeSymbol named && IsJint(named);

    /// <summary>
    /// The first call or member, reached from the expression, that comes from a non-test assembly other than Jint,
    /// <c>JsEngineFactory.Create()</c> and <c>ScriptHost</c>; null when the engine or promise can only come from the
    /// test projects' own constructions. A local is followed to its initialiser.
    /// </summary>
    private static string? ForeignOrigin(SemanticModel model, SyntaxNode expression, int depth)
    {
        foreach (var node in expression.DescendantNodesAndSelf())
        {
            ISymbol? symbol = node switch
            {
                InvocationExpressionSyntax i => ResolveMethod(model, i),
                BaseObjectCreationExpressionSyntax c => model.GetSymbolInfo(c).Symbol,
                MemberAccessExpressionSyntax m => model.GetSymbolInfo(m).Symbol,
                _ => null,
            };
            if (symbol is null && node is IdentifierNameSyntax id && depth < 4
                && model.GetSymbolInfo(id).Symbol is ILocalSymbol local)
            {
                foreach (var reference in local.DeclaringSyntaxReferences)
                    if (reference.GetSyntax() is VariableDeclaratorSyntax { Initializer: { } init } && ForeignOrigin(model, init.Value, depth + 1) is { } found)
                        return found;
                continue;
            }
            if (symbol is null || symbol.Locations.Any(l => l.IsInSource)) continue;
            var owner = symbol as INamedTypeSymbol ?? symbol.ContainingType;
            if (owner is null || IsJint(owner)) continue;
            if (IsNamed(owner, "Orkeon.Scripting", "JsEngineFactory") && symbol.Name == "Create") continue;
            if (IsNamed(owner, "Orkeon.Scripting", "ScriptHost")) continue;
            var produced = symbol switch
            {
                IMethodSymbol { MethodKind: MethodKind.Constructor } => null,
                IMethodSymbol m => m.ReturnType,
                IPropertySymbol p => p.Type,
                IFieldSymbol f => f.Type,
                _ => null,
            };
            if (produced is null || UnwrapTask(produced) is not { } t || !IsNamed(t, "Jint", "Engine")) continue;
            return owner.ToDisplayString() + "." + symbol.Name;
        }
        return null;
    }

    private static ITypeSymbol? UnwrapTask(ITypeSymbol type)
        => type is INamedTypeSymbol { IsGenericType: true, Name: "Task" or "ValueTask" } g ? g.TypeArguments[0] : type;

    private sealed class ProjectScan
    {
        public bool Assigned;
        public readonly List<(string Path, int Line)> CreateSites = [];
        public readonly List<(string Path, int Line)> ScriptHostSites = [];
    }

    /// <summary>Whether the statement sits in a method carrying the resolved <c>ModuleInitializerAttribute</c>.</summary>
    private static bool InModuleInitializer(SemanticModel model, SyntaxNode node)
        => node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() is { } method
           && model.GetDeclaredSymbol(method) is { } symbol
           && symbol.GetAttributes().Any(a => a.AttributeClass is { Name: "ModuleInitializerAttribute" } c
                                              && c.ContainingNamespace.ToDisplayString() == "System.Runtime.CompilerServices");

    /// <summary>A statement whose resolved target is the internal <c>JsEngineFactory.PromiseTimeout</c>, assigned the resolved <c>HangGuards.EnginePromise</c>.</summary>
    private static bool IsFactorySetting(SemanticModel model, AssignmentExpressionSyntax a)
        => model.GetSymbolInfo(a.Left).Symbol is IPropertySymbol { Name: "PromiseTimeout" } property
           && IsNamed(property.ContainingType, "Orkeon.Scripting", "JsEngineFactory")
           && model.GetSymbolInfo(a.Right).Symbol is IFieldSymbol { Name: "EnginePromise" } field
           && IsNamed(field.ContainingType, "Orkeon.Tests.Shared.Timing", "HangGuards");

    private static string? InvokedName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        MemberBindingExpressionSyntax b => b.Name.Identifier.ValueText,
        IdentifierNameSyntax i => i.Identifier.ValueText,
        _ => null,
    };

    private static IMethodSymbol? ResolveMethod(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        var info = model.GetSymbolInfo(invocation);
        return info.Symbol as IMethodSymbol ?? info.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
    }

    private static bool IsNamed(ITypeSymbol type, string ns, string name)
        => type.Name == name && type.ContainingNamespace?.ToDisplayString() == ns;

    private static bool IsJint(INamedTypeSymbol? type)
    {
        if (type is null) return false;
        var ns = type.ContainingNamespace;
        while (ns is { IsGlobalNamespace: false, ContainingNamespace: { IsGlobalNamespace: false } parent })
            ns = parent;
        return ns is not null && ns.Name == "Jint";
    }

    private static Compilation BuildCompilation(string root, string project)
    {
        var projectDir = Path.Combine(root, project.Replace('/', Path.DirectorySeparatorChar));
        var tfmDir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var outputDir = Path.Combine(projectDir, "bin", tfmDir.Parent!.Name, tfmDir.Name);
        if (!Directory.Exists(outputDir))
            Assert.Fail($"{outputDir} does not exist: build the solution first.");

        var trees = Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !IsBuildOutput(projectDir, p))
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => CSharpSyntaxTree.ParseText(File.ReadAllText(p), ParseOptions, path: Path.GetRelativePath(root, p)))
            .Append(CSharpSyntaxTree.ParseText("""
                global using global::System;
                global using global::System.Collections.Generic;
                global using global::System.IO;
                global using global::System.Linq;
                global using global::System.Net.Http;
                global using global::System.Threading;
                global using global::System.Threading.Tasks;
                global using global::Xunit;
                """, ParseOptions, path: "ImplicitUsings.g.cs"));

        var own = Path.GetFileName(projectDir) + ".dll";
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new List<string>();
        var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        foreach (var p in trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            if (File.Exists(p) && names.Add(Path.GetFileName(p))) paths.Add(p);
        foreach (var p in Directory.EnumerateFiles(outputDir, "*.dll").OrderBy(p => p, StringComparer.Ordinal))
        {
            var file = Path.GetFileName(p);
            if (file.Equals(own, StringComparison.OrdinalIgnoreCase) || !names.Add(file)) continue;
            try { AssemblyName.GetAssemblyName(p); }
            catch (BadImageFormatException) { continue; }
            paths.Add(p);
        }

        return CSharpCompilation.Create(
            Path.GetFileName(projectDir),
            trees,
            paths.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Orkeon.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Orkeon.sln not found above " + AppContext.BaseDirectory);
    }

    private static bool IsBuildOutput(string project, string path)
        => Path.GetRelativePath(project, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(s => s.Equals("bin", StringComparison.OrdinalIgnoreCase)
                      || s.Equals("obj", StringComparison.OrdinalIgnoreCase)
                      || s.StartsWith("obj-", StringComparison.OrdinalIgnoreCase));
}
