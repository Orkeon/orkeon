using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Orkeon.Scripting.Tests.Architecture;

/// <summary>
/// The engine threading rule of SCR-25 (sheet section 3), enforced on the sources of
/// <c>src/scripting/Orkeon.Scripting</c> with Roslyn so the family of defects the chantier
/// removed cannot come back. A CLR delegate exposed to the script may (a) return a value
/// synchronously, (b) return a <see cref="Task"/> whose result is a CLR value, or (c) return
/// a JS promise obtained synchronously; it must NEVER call <c>Engine.Invoke</c> /
/// <c>Evaluate</c> / <c>Execute</c> / <c>SetValue</c> / <c>JsValue.FromObject</c> /
/// <c>Call</c> after an <c>await</c> — the continuation runs on a thread-pool thread while
/// another may be draining the engine — and never drain (<c>UnwrapIfPromise*</c>) from inside
/// an event-loop job. The only drains are the three root pumps — <c>ScriptHost.EvaluateAsync</c>,
/// <c>JsCrew.Pump</c>, <c>JsTool.CallAsync</c> — each on the
/// <c>UnwrapIfPromise(CancellationToken)</c> overload, inside one synchronous <c>Task.Run</c>
/// lambda holding <c>JsEngineGate</c>. A third rule guards the trampoline factories (T4 design,
/// fact F2): <c>Engine.Evaluate</c> drains queued jobs on its way out, so a factory evaluated
/// lazily from a script's synchronous prefix runs microtasks mid-statement; every factory is
/// evaluated by <c>JsTrampolineFactories</c> with the engine at rest, and nothing else evaluates.
/// </summary>
/// <remarks>
/// The analyser is exercised on inline snippets too (one violation of each kind, and clean
/// shapes it must accept), so a silently broken analyser cannot pass the repository scan.
/// </remarks>
public sealed class EngineThreadingGuardTests
{
    private const string Sheet = "the SCR-25 task sheet, section 3 (the engine threading rule)";

    private static readonly Lazy<Compilation> Sources = new(BuildSourceCompilation);

    [Fact]
    public void Sources_never_call_into_the_engine_after_an_await()
    {
        var violations = EngineThreadingAnalyzer.Analyze(Sources.Value)
            .Where(v => v.Rule == EngineThreadingAnalyzer.EntryAfterAwait).ToList();

        Assert.True(violations.Count == 0,
            "A CLR continuation must never enter the engine: no Engine.Invoke/Evaluate/Execute/SetValue, " +
            "JsValue.FromObject/Call after an await in an async method, async local function or async lambda " +
            $"(a JS trampoline is the shape — {Sheet}). Violations:\n" + Report(violations));
    }

    [Fact]
    public void Sources_drain_only_in_the_three_root_pumps()
    {
        var violations = EngineThreadingAnalyzer.Analyze(Sources.Value)
            .Where(v => v.Rule == EngineThreadingAnalyzer.DrainOutsideRootPump).ToList();

        Assert.True(violations.Count == 0,
            "The only drains are the three root pumps (ScriptHost.EvaluateAsync, JsCrew.Pump, JsTool.CallAsync), " +
            $"each on the synchronous UnwrapIfPromise(CancellationToken) overload — {Sheet}. Violations:\n" + Report(violations));
    }

    [Fact]
    public void Sources_evaluate_only_in_the_root_pump_and_the_factory_store()
    {
        var violations = EngineThreadingAnalyzer.Analyze(Sources.Value)
            .Where(v => v.Rule == EngineThreadingAnalyzer.EvaluateOutsideStore).ToList();

        Assert.True(violations.Count == 0,
            "Engine.Evaluate drains queued event-loop jobs on its way out (T4 design, F2): a trampoline factory " +
            "belongs in JsTrampolineFactories, evaluated with the engine at rest; only ScriptHost evaluates otherwise. " +
            "Violations:\n" + Report(violations));
    }

    /// <summary>
    /// Positive control: one violation of each kind, on a known line, in a snippet compiled against
    /// the same references. A rule the analyser stopped reporting fails here before it could let a
    /// regression through the scan.
    /// </summary>
    [Fact]
    public void Analyser_reports_each_kind_of_violation()
    {
        const string snippet = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Jint;
            using Jint.Native;

            sealed class Probe
            {
                private readonly Engine _engine = new();

                public async Task InvokeAfterAwait(JsValue fn) { await Task.Yield(); _engine.Invoke(fn); }

                public async Task InvokeInsideALoopThatAwaits(JsValue fn)
                {
                    for (var i = 0; i < 2; i++) { _engine.Invoke(fn); await Task.Yield(); }
                }

                public async Task NestedSynchronousLambdaCountsAsTheBody(JsValue fn)
                {
                    await Task.Yield();
                    Func<JsValue> f = () => _engine.Invoke(fn);
                    f();
                }

                public Func<Task> AsyncLambdaIsABody(JsValue fn) => async () => { await Task.Yield(); JsValue.FromObject(_engine, 1); };

                public async Task SetValueAfterAwaitInALocalFunction()
                {
                    await Task.Yield();
                    async Task Local() { await Task.Yield(); _engine.SetValue("x", 1); }
                    await Local();
                }

                public JsValue DrainOutsideAPump(JsValue p) => p.UnwrapIfPromise(CancellationToken.None);

                public JsValue EvaluateOutsideTheStore() => _engine.Evaluate("1");
            }

            sealed class ScriptHost
            {
                private static async Task EvaluateAsync(JsValue p, CancellationToken ct)
                {
                    await Task.Yield();
                    await Task.Run(() => p.UnwrapIfPromise(TimeSpan.FromSeconds(1)), ct);
                    await p.UnwrapIfPromiseAsync(ct);
                }
            }
            """;

        var violations = EngineThreadingAnalyzer.Analyze(Compile("Probe.cs", snippet));

        var expected = new (string Rule, int Line)[]
        {
            (EngineThreadingAnalyzer.EntryAfterAwait, 11),
            (EngineThreadingAnalyzer.EntryAfterAwait, 15),
            (EngineThreadingAnalyzer.EntryAfterAwait, 21),
            (EngineThreadingAnalyzer.EntryAfterAwait, 25),
            (EngineThreadingAnalyzer.EntryAfterAwait, 30),
            (EngineThreadingAnalyzer.DrainOutsideRootPump, 34),
            (EngineThreadingAnalyzer.EvaluateOutsideStore, 36),
            (EngineThreadingAnalyzer.DrainOutsideRootPump, 44),
            (EngineThreadingAnalyzer.DrainOutsideRootPump, 45),
        };
        Assert.Equal(expected, violations.Select(v => (v.Rule, v.Line)).OrderBy(v => v.Line).ThenBy(v => v.Rule).ToArray());
    }

    /// <summary>
    /// Negative control: the shapes the rule allows — an entry before the await, an async lambda
    /// analysed on its own (the outer await does not taint it), and the root pump itself: the
    /// synchronous <c>Task.Run</c> lambda of an allow-listed member, on the CancellationToken overload.
    /// </summary>
    [Fact]
    public void Analyser_accepts_the_allowed_shapes()
    {
        const string snippet = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Jint;
            using Jint.Native;

            sealed class Probe
            {
                private readonly Engine _engine = new();

                public async Task InvokeBeforeTheAwait(JsValue fn) { _engine.Invoke(fn); await Task.Yield(); }

                public async Task AsyncLambdaIsItsOwnBody(JsValue fn)
                {
                    await Task.Yield();
                    Func<Task> f = async () => { _engine.Invoke(fn); await Task.Yield(); };
                    await f();
                }

                public async Task DelegateInvokeIsNotTheEngine(Func<int> f) { await Task.Yield(); f.Invoke(); }
            }

            sealed class JsTool
            {
                public async Task<object?> CallAsync(Engine engine, JsValue fn, CancellationToken ct)
                {
                    await Task.Yield();
                    return await Task.Run(() =>
                    {
                        var input = JsValue.FromObject(engine, 1);
                        var result = engine.Invoke(fn, input);
                        return Jint.JsValueExtensions.UnwrapIfPromise(result, ct).ToObject();
                    }, ct);
                }
            }

            sealed class JsTrampolineFactories
            {
                public static JsValue For(Engine engine) => engine.Evaluate("(x) => x");
            }
            """;

        var violations = EngineThreadingAnalyzer.Analyze(Compile("Probe.cs", snippet));

        Assert.True(violations.Count == 0, "Unexpected violations:\n" + Report(violations));
    }

    private static string Report(IEnumerable<EngineThreadingViolation> violations)
        => string.Join("\n", violations.Select(v => "  " + v));

    // ---- the compilation over src/scripting/Orkeon.Scripting ----

    private static Compilation BuildSourceCompilation()
    {
        var root = RepoRoot();
        var project = Path.Combine(root, "src", "scripting", "Orkeon.Scripting");
        var trees = Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(project, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), ParseOptions, path: Path.GetRelativePath(root, path)))
            .ToList();
        Assert.NotEmpty(trees);
        return Compile(trees);
    }

    private static Compilation Compile(string fileName, string source)
        => Compile([CSharpSyntaxTree.ParseText(source, ParseOptions, path: fileName)]);

    private static Compilation Compile(IEnumerable<SyntaxTree> trees)
    {
        // The SDK's implicit usings, which the sources rely on and a bare compilation lacks.
        var implicitUsings = CSharpSyntaxTree.ParseText("""
            global using global::System;
            global using global::System.Collections.Generic;
            global using global::System.IO;
            global using global::System.Linq;
            global using global::System.Net.Http;
            global using global::System.Threading;
            global using global::System.Threading.Tasks;
            """, ParseOptions, path: "ImplicitUsings.g.cs");
        return CSharpCompilation.Create(
            "EngineThreadingGuard",
            trees.Append(implicitUsings),
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    /// <summary>
    /// Every assembly the runtime trusts for this process plus the closure of the scripting
    /// assembly's references, so that a receiver's type resolves (Engine, JsValue, a delegate's
    /// Invoke) instead of falling back to the name alone.
    /// </summary>
    private static readonly Lazy<IReadOnlyList<MetadataReference>> References = new(() =>
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        foreach (var path in trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            if (File.Exists(path)) paths.Add(path);
        foreach (var assembly in ReferenceClosure(typeof(ScriptHost).Assembly))
            if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location)) paths.Add(assembly.Location);
        return paths.OrderBy(p => p, StringComparer.Ordinal).Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
    });

    private static IEnumerable<Assembly> ReferenceClosure(Assembly root)
    {
        var seen = new Dictionary<string, Assembly>(StringComparer.Ordinal) { [root.FullName!] = root };
        var pending = new Queue<Assembly>();
        pending.Enqueue(root);
        while (pending.TryDequeue(out var current))
        {
            foreach (var name in current.GetReferencedAssemblies())
            {
                if (seen.ContainsKey(name.FullName)) continue;
                Assembly loaded;
                try { loaded = Assembly.Load(name); }
                catch (FileNotFoundException) { continue; }
                catch (FileLoadException) { continue; }
                seen[name.FullName] = loaded;
                pending.Enqueue(loaded);
            }
        }
        return seen.Values;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Orkeon.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Orkeon.sln not found above " + AppContext.BaseDirectory);
    }

    /// <summary>Build output lives in <c>bin*</c> / <c>obj*</c> folders under the project (the Linux intermediate folder is <c>obj-linux</c>).</summary>
    private static bool IsBuildOutput(string project, string path)
        => Path.GetRelativePath(project, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment.StartsWith("bin", StringComparison.OrdinalIgnoreCase) || segment.StartsWith("obj", StringComparison.OrdinalIgnoreCase));
}

internal sealed record EngineThreadingViolation(string Rule, string File, int Line, string Detail)
{
    public override string ToString() => $"{File}:{Line} [{Rule}] {Detail}";
}

/// <summary>
/// The three rules over a compilation. Receiver types come from the semantic model; when a
/// symbol cannot be resolved the member name alone decides, on the conservative side.
/// </summary>
internal static class EngineThreadingAnalyzer
{
    public const string EntryAfterAwait = "entry-after-await";
    public const string DrainOutsideRootPump = "drain-outside-root-pump";
    public const string EvaluateOutsideStore = "evaluate-outside-store";

    private static readonly HashSet<string> EntryNames = new(StringComparer.Ordinal) { "Invoke", "Evaluate", "FromObject", "SetValue", "Execute", "Call" };
    private static readonly HashSet<string> DrainNames = new(StringComparer.Ordinal) { "UnwrapIfPromise", "UnwrapIfPromiseAsync" };

    /// <summary>The root pumps, by containing type and containing method declaration.</summary>
    private static readonly (string Type, string Member)[] RootPumps =
    [
        ("ScriptHost", "EvaluateAsync"),
        ("JsCrew", "Pump"),
        ("JsTool", "CallAsync"),
    ];

    /// <summary>The types allowed to call <c>Engine.Evaluate</c>: the script's own root pump and the factory store.</summary>
    private static readonly string[] EvaluateOwners = ["ScriptHost", "JsTrampolineFactories"];

    public static IReadOnlyList<EngineThreadingViolation> Analyze(Compilation compilation)
    {
        var violations = new List<EngineThreadingViolation>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            foreach (var owner in root.DescendantNodes().Where(IsAsyncBody))
                CheckEntriesAfterAwait(model, owner, violations);
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var name = InvokedName(invocation);
                if (name is null) continue;
                if (DrainNames.Contains(name))
                    CheckDrain(model, invocation, name, violations);
                else if (string.Equals(name, "Evaluate", StringComparison.Ordinal) && IsEngineEntry(model, invocation))
                    CheckEvaluateOwner(invocation, violations);
            }
        }
        return violations.OrderBy(v => v.File, StringComparer.Ordinal).ThenBy(v => v.Line).ToList();
    }

    // ---- rule 1: no engine entry after an await of the same async body ----

    private static void CheckEntriesAfterAwait(SemanticModel model, SyntaxNode owner, List<EngineThreadingViolation> violations)
    {
        var body = BodyOf(owner);
        if (body is null) return;
        var nodes = body.DescendantNodesAndSelf(descendIntoChildren: n => ReferenceEquals(n, body) || !IsSeparateBody(n)).ToList();
        var awaits = nodes.OfType<AwaitExpressionSyntax>().ToList();
        if (awaits.Count == 0) return;
        foreach (var invocation in nodes.OfType<InvocationExpressionSyntax>())
        {
            var name = InvokedName(invocation);
            if (name is null || !EntryNames.Contains(name) || !IsEngineEntry(model, invocation)) continue;
            var afterAwait = awaits.Any(a => a.SpanStart < invocation.SpanStart);
            var inAwaitingLoop = LoopsBetween(invocation, body).Any(loop => awaits.Any(a => loop.Span.Contains(a.Span)));
            if (afterAwait || inAwaitingLoop)
                violations.Add(Violation(EntryAfterAwait, invocation,
                    $"{name} after an await in {Describe(owner)}" + (inAwaitingLoop && !afterAwait ? " (inside a loop that awaits)" : "")));
        }
    }

    private static bool IsAsyncBody(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax m => m.Modifiers.Any(SyntaxKind.AsyncKeyword),
        LocalFunctionStatementSyntax f => f.Modifiers.Any(SyntaxKind.AsyncKeyword),
        AnonymousFunctionExpressionSyntax l => l.Modifiers.Any(SyntaxKind.AsyncKeyword),
        _ => false,
    };

    /// <summary>
    /// A nested async body is analysed on its own; the synchronous <c>Task.Run</c> lambda of an
    /// allow-listed root pump is the pump itself — a fresh pool thread with the engine at rest and
    /// the gate held — and not a continuation of the enclosing method.
    /// </summary>
    private static bool IsSeparateBody(SyntaxNode node) => IsAsyncBody(node) || IsRootPumpLambda(node);

    private static bool IsRootPumpLambda(SyntaxNode node)
    {
        if (node is not AnonymousFunctionExpressionSyntax { Parent: ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax call } } })
            return false;
        if (call.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Run", Expression: IdentifierNameSyntax { Identifier.ValueText: "Task" } })
            return false;
        return IsRootPump(node);
    }

    private static SyntaxNode? BodyOf(SyntaxNode owner) => owner switch
    {
        MethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody,
        LocalFunctionStatementSyntax f => (SyntaxNode?)f.Body ?? f.ExpressionBody,
        AnonymousFunctionExpressionSyntax l => l.Body,
        _ => null,
    };

    private static IEnumerable<SyntaxNode> LoopsBetween(SyntaxNode node, SyntaxNode body)
    {
        for (var current = node.Parent; current is not null && !ReferenceEquals(current, body); current = current.Parent)
        {
            if (current is ForStatementSyntax or ForEachStatementSyntax or ForEachVariableStatementSyntax or WhileStatementSyntax or DoStatementSyntax)
                yield return current;
        }
    }

    // ---- rule 2: UnwrapIfPromise only in a root pump, on the CancellationToken overload ----

    private static void CheckDrain(SemanticModel model, InvocationExpressionSyntax invocation, string name, List<EngineThreadingViolation> violations)
    {
        if (!IsRootPump(invocation))
        {
            violations.Add(Violation(DrainOutsideRootPump, invocation, $"{name} outside the three root pumps ({Location(invocation)})"));
            return;
        }
        if (!string.Equals(name, "UnwrapIfPromise", StringComparison.Ordinal))
        {
            violations.Add(Violation(DrainOutsideRootPump, invocation, $"{name}: a root pump drains synchronously, on UnwrapIfPromise(CancellationToken)"));
            return;
        }
        if (!IsCancellationTokenOverload(model, invocation))
            violations.Add(Violation(DrainOutsideRootPump, invocation, "UnwrapIfPromise on an overload other than UnwrapIfPromise(CancellationToken): a root pump is bounded by its token, not by a timeout"));
    }

    private static bool IsCancellationTokenOverload(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        if (ResolveMethod(model, invocation) is { } method)
        {
            return method.Parameters.Any(p => IsCancellationToken(p.Type))
                && !method.Parameters.Any(p => string.Equals(p.Type.ToDisplayString(), "System.TimeSpan", StringComparison.Ordinal));
        }
        var last = invocation.ArgumentList.Arguments.LastOrDefault();
        return last is not null && model.GetTypeInfo(last.Expression).Type is { } type && IsCancellationToken(type);
    }

    private static bool IsCancellationToken(ITypeSymbol type)
        => string.Equals(type.ToDisplayString(), "System.Threading.CancellationToken", StringComparison.Ordinal);

    // ---- rule 3: Engine.Evaluate only in ScriptHost and the factory store ----

    private static void CheckEvaluateOwner(InvocationExpressionSyntax invocation, List<EngineThreadingViolation> violations)
    {
        // Any enclosing type counts: the store keeps its per-factory state in a nested class.
        if (invocation.Ancestors().OfType<TypeDeclarationSyntax>().Any(t => EvaluateOwners.Contains(t.Identifier.ValueText, StringComparer.Ordinal))) return;
        violations.Add(Violation(EvaluateOutsideStore, invocation, $"Engine.Evaluate in {ContainingTypeName(invocation) ?? "<no type>"}: a trampoline factory belongs in JsTrampolineFactories"));
    }

    // ---- shared ----

    private static bool IsRootPump(SyntaxNode node)
    {
        var type = ContainingTypeName(node);
        var member = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;
        return type is not null && member is not null
            && RootPumps.Any(p => string.Equals(p.Type, type, StringComparison.Ordinal) && string.Equals(p.Member, member, StringComparison.Ordinal));
    }

    private static string? ContainingTypeName(SyntaxNode node)
        => node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;

    private static string Location(SyntaxNode node)
    {
        var type = ContainingTypeName(node) ?? "<no type>";
        var member = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "<no method>";
        return type + "." + member;
    }

    private static string? InvokedName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        MemberBindingExpressionSyntax b => b.Name.Identifier.ValueText,
        IdentifierNameSyntax i => i.Identifier.ValueText,
        GenericNameSyntax g => g.Identifier.ValueText,
        _ => null,
    };

    private static ExpressionSyntax? ReceiverOf(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax m => m.Expression,
        MemberBindingExpressionSyntax b => b.Ancestors().OfType<ConditionalAccessExpressionSyntax>().FirstOrDefault()?.Expression,
        _ => null,
    };

    private static IMethodSymbol? ResolveMethod(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        var info = model.GetSymbolInfo(invocation);
        return info.Symbol as IMethodSymbol ?? info.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
    }

    /// <summary>
    /// Whether the invoked member is Jint's: the method's containing type (the reduced form of an
    /// extension method included) or, failing that, the receiver's type lives in a <c>Jint</c>
    /// namespace. An unresolvable receiver counts — the name alone decides then.
    /// </summary>
    private static bool IsEngineEntry(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        if (ResolveMethod(model, invocation) is { } method)
            return IsJintType(method.ReducedFrom?.ContainingType ?? method.ContainingType);
        if (ReceiverOf(invocation) is { } receiver)
        {
            var type = model.GetTypeInfo(receiver).Type ?? model.GetSymbolInfo(receiver).Symbol as ITypeSymbol;
            if (type is not null && type.TypeKind != TypeKind.Error)
                return IsJintType(type);
        }
        return true;
    }

    private static bool IsJintType(INamedTypeSymbol? type)
    {
        if (type is null) return false;
        var ns = type.ContainingNamespace;
        while (ns is { IsGlobalNamespace: false, ContainingNamespace: { IsGlobalNamespace: false } parent })
            ns = parent;
        return ns is not null && string.Equals(ns.Name, "Jint", StringComparison.Ordinal);
    }

    private static bool IsJintType(ITypeSymbol type) => IsJintType(type as INamedTypeSymbol ?? type.BaseType);

    private static string Describe(SyntaxNode owner) => owner switch
    {
        MethodDeclarationSyntax m => "async method " + m.Identifier.ValueText,
        LocalFunctionStatementSyntax f => "async local function " + f.Identifier.ValueText,
        _ => "an async lambda of " + Location(owner),
    };

    private static EngineThreadingViolation Violation(string rule, SyntaxNode node, string detail)
    {
        var span = node.GetLocation().GetLineSpan();
        return new EngineThreadingViolation(rule, span.Path, span.StartLinePosition.Line + 1, detail);
    }
}
