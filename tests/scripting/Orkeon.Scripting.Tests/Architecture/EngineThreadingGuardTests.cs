using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Orkeon.Scripting.Tests.Architecture;

/// <summary>
/// The engine threading rule of SCR-25 (sheet section 3), enforced on the sources of
/// <c>src/scripting/Orkeon.Scripting</c> with Roslyn so the family of defects the chantier
/// removed cannot come back. A CLR delegate exposed to the script may (a) return a value
/// synchronously, (b) return a <see cref="Task"/> whose result is a CLR value, or (c) return
/// a JS promise obtained synchronously; it must NEVER call <c>Engine.Invoke</c> /
/// <c>Evaluate</c> / <c>Execute</c> / <c>SetValue</c> / <c>JsValue.FromObject</c> /
/// <c>Call</c> after an <c>await</c> — an <c>await</c> expression, an <c>await foreach</c>
/// or an <c>await using</c>, an await nested in the entry's own receiver or arguments included —
/// because the continuation runs on a thread-pool thread while another may be draining the
/// engine; and never drain (<c>UnwrapIfPromise*</c>) from inside an event-loop job. The only
/// drains are the three root pumps — <c>ScriptHost.EvaluateAsync</c>, <c>JsCrew.Pump</c>,
/// <c>JsTool.CallAsync</c> — each on the <c>UnwrapIfPromise(CancellationToken)</c> overload,
/// inside the pump's synchronous body (its <c>Task.Run</c> lambda, or the synchronous method
/// <c>Task.Run</c> dispatches) holding <c>JsEngineGate</c>. A third rule guards the trampoline
/// factories (T4 design, fact F2): <c>Engine.Evaluate</c> drains queued jobs on its way out, so
/// a factory evaluated lazily from a script's synchronous prefix runs microtasks mid-statement;
/// every factory is evaluated by <c>JsTrampolineFactories</c> with the engine at rest, and
/// nothing else evaluates.
/// </summary>
/// <remarks>
/// The analyser is exercised on inline snippets too (one violation of each kind, and clean
/// shapes it must accept), so a silently broken analyser cannot pass the repository scan; and
/// the scan's file filter is checked against the compiled assembly, so a filter that drops a
/// folder cannot silently shrink the guard.
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
            "JsValue.FromObject/Call after an await (expression, foreach or using) in an async method, async local " +
            $"function or async lambda (a JS trampoline is the shape — {Sheet}). Violations:\n" + Report(violations));
    }

    [Fact]
    public void Sources_drain_only_in_the_three_root_pumps()
    {
        var violations = EngineThreadingAnalyzer.Analyze(Sources.Value)
            .Where(v => v.Rule == EngineThreadingAnalyzer.DrainOutsideRootPump).ToList();

        Assert.True(violations.Count == 0,
            "The only drains are the three root pumps (ScriptHost.EvaluateAsync, JsCrew.Pump, JsTool.CallAsync), " +
            $"each on the synchronous UnwrapIfPromise(CancellationToken) overload inside the pump's synchronous body — {Sheet}. " +
            "Violations:\n" + Report(violations));
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
    /// The scan's file filter against an independent oracle: every top-level type the compiled
    /// scripting assembly declares must be declared in the scanned sources — asked of the source
    /// assembly symbol, not the compilation, which would also answer from a metadata reference. A
    /// filter that silently drops a folder — a segment prefix once excluded <c>Bindings/</c> along
    /// with <c>bin/</c> — fails here instead of shrinking the guard to the files it happens to see.
    /// </summary>
    [Fact]
    public void Scan_covers_every_type_the_scripting_assembly_declares()
    {
        var compilation = Sources.Value;
        var missing = typeof(ScriptHost).Assembly.GetTypes()
            .Where(t => !t.IsNested && t.FullName is { } name && !name.Contains('<', StringComparison.Ordinal))
            .Where(t => !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false)
                        && !t.IsDefined(typeof(System.CodeDom.Compiler.GeneratedCodeAttribute), inherit: false))
            .Select(t => t.FullName!)
            .Where(name => compilation.Assembly.GetTypeByMetadataName(name) is null)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            "The guard scans fewer sources than the assembly is built from — its file filter dropped the files declaring:\n  "
            + string.Join("\n  ", missing));
    }

    /// <summary>
    /// Positive control: one violation of each kind, on the line that carries the matching
    /// <c>// expect:</c> marker, in a snippet compiled without errors against the same references
    /// (so every receiver resolves and the rules run on the semantic path, not the name fallback).
    /// A rule the analyser stopped reporting fails here before it could let a regression through.
    /// </summary>
    [Fact]
    public void Analyser_reports_each_kind_of_violation()
    {
        const string snippet = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Jint;
            using Jint.Native;

            sealed class Probe
            {
                private readonly Engine _engine = new();

                public async Task InvokeAfterAwait(JsValue fn) { await Task.Yield(); _engine.Invoke(fn); }                        // expect: entry-after-await

                public async Task ExecuteAfterAwait() { await Task.Yield(); _engine.Execute("1"); }                              // expect: entry-after-await

                public async Task CallAfterAwait(JsValue fn) { await Task.Yield(); fn.Call(JsValue.Undefined); }                 // expect: entry-after-await

                public async Task InvokeInsideALoopThatAwaits(JsValue fn)
                {
                    for (var i = 0; i < 2; i++) { _engine.Invoke(fn); await Task.Yield(); }                                       // expect: entry-after-await
                }

                public async Task NestedSynchronousLambdaCountsAsTheBody(JsValue fn)
                {
                    await Task.Yield();
                    Func<JsValue> f = () => _engine.Invoke(fn);                                                                   // expect: entry-after-await
                    f();
                }

                public Func<Task> AsyncLambdaIsABody(JsValue fn) => async () => { await Task.Yield(); JsValue.FromObject(_engine, 1); }; // expect: entry-after-await

                public async Task SetValueAfterAwaitInALocalFunction()
                {
                    await Task.Yield();
                    async Task Local() { await Task.Yield(); _engine.SetValue("x", 1); }                                          // expect: entry-after-await
                    await Local();
                }

                public async Task InvokeInsideAnAwaitForeach(JsValue fn, IAsyncEnumerable<int> items)
                {
                    await foreach (var item in items) _engine.Invoke(fn, item);                                                   // expect: entry-after-await
                }

                public async Task InvokeAfterAnAwaitUsingBlock(JsValue fn, IAsyncDisposable scope)
                {
                    await using (scope) { }
                    _engine.Invoke(fn);                                                                                           // expect: entry-after-await
                }

                public async Task InvokeAfterAnAwaitUsingDeclarationLeftItsBlock(JsValue fn, IAsyncDisposable scope)
                {
                    { await using var held = scope; }
                    _engine.Invoke(fn);                                                                                           // expect: entry-after-await
                }

                public async Task AwaitNestedInTheEntrysArguments() => JsValue.FromObject(_engine, await Task.FromResult(1));      // expect: entry-after-await

                public async Task AwaitNestedInTheEntrysReceiver(JsValue fn) => (await Task.FromResult(_engine)).Invoke(fn);       // expect: entry-after-await

                public JsValue DrainOutsideAPump(JsValue p) => p.UnwrapIfPromise(CancellationToken.None);                         // expect: drain-outside-root-pump

                public JsValue EvaluateOutsideTheStore() => _engine.Evaluate("1");                                                // expect: evaluate-outside-store
            }

            namespace Orkeon.Scripting
            {
                sealed class ScriptHost
                {
                    private static async Task EvaluateAsync(JsValue p, CancellationToken ct)
                    {
                        await Task.Yield();
                        await Task.Run(() => p.UnwrapIfPromise(TimeSpan.FromSeconds(1)), ct);                                    // expect: drain-outside-root-pump
                        await Task.Run(() => p.UnwrapIfPromise(), ct);                                                           // expect: drain-outside-root-pump
                        await p.UnwrapIfPromiseAsync(ct);                                                                        // expect: drain-outside-root-pump
                        await Task.Run(async () => { await Task.Yield(); return p.UnwrapIfPromise(ct); }, ct);                   // expect: drain-outside-root-pump
                        await Task.Run(() => { Func<JsValue> inner = () => p.UnwrapIfPromise(ct); return inner(); }, ct);        // expect: drain-outside-root-pump
                    }
                }
            }

            namespace Elsewhere
            {
                sealed class ScriptHost
                {
                    private static async Task EvaluateAsync(JsValue p, CancellationToken ct)
                    {
                        await Task.Run(() => p.UnwrapIfPromise(ct), ct);                                                         // expect: drain-outside-root-pump
                    }
                }
            }
            """;

        var violations = EngineThreadingAnalyzer.Analyze(CompileClean("Probe.cs", snippet));

        Assert.Equal(ExpectedMarkers(snippet), violations.Select(v => (v.Rule, v.Line)).OrderBy(v => v.Line).ThenBy(v => v.Rule).ToArray());
    }

    /// <summary>
    /// Negative control: the shapes the rule allows — an entry before the await (an <c>await foreach</c>
    /// included), an entry inside an <c>await using</c> block before its dispose, an async lambda
    /// analysed on its own (the outer await does not taint it), a delegate's own <c>Invoke</c>, and the
    /// three root pumps themselves: the synchronous <c>Task.Run</c> lambda of <c>ScriptHost.EvaluateAsync</c>
    /// and <c>JsTool.CallAsync</c>, the synchronous <c>JsCrew.Pump</c>, on the CancellationToken overload.
    /// </summary>
    [Fact]
    public void Analyser_accepts_the_allowed_shapes()
    {
        const string snippet = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Jint;
            using Jint.Native;

            sealed class Probe
            {
                private readonly Engine _engine = new();

                public async Task InvokeBeforeTheAwait(JsValue fn) { _engine.Invoke(fn); await Task.Yield(); }

                public async Task InvokeBeforeTheAwaitForeach(JsValue fn, IAsyncEnumerable<int> items)
                {
                    _engine.Invoke(fn);
                    await foreach (var item in items) { }
                }

                public async Task InvokeInsideAnAwaitUsingBlockBeforeItsDispose(JsValue fn, IAsyncDisposable scope)
                {
                    await using (scope) { _engine.Invoke(fn); }
                }

                public async Task AsyncLambdaIsItsOwnBody(JsValue fn)
                {
                    await Task.Yield();
                    Func<Task> f = async () => { _engine.Invoke(fn); await Task.Yield(); };
                    await f();
                }

                public async Task DelegateInvokeIsNotTheEngine(Func<int> f) { await Task.Yield(); f.Invoke(); }
            }

            namespace Orkeon.Scripting
            {
                sealed class ScriptHost
                {
                    private static async Task<JsValue> EvaluateAsync(Engine engine, JsValue p, CancellationToken ct)
                    {
                        await Task.Yield();
                        return await Task.Run(() =>
                        {
                            engine.SetValue("x", 1);
                            var value = engine.Evaluate("x");
                            return Jint.JsValueExtensions.UnwrapIfPromise(value, ct);
                        }, ct);
                    }
                }
            }

            namespace Orkeon.Scripting.Runtime
            {
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

                sealed class JsCrew
                {
                    public Task<object?> RunAsync(Engine engine, JsValue fn, CancellationToken ct) => Task.Run(() => Pump(engine, fn, ct), ct);

                    private static object? Pump(Engine engine, JsValue fn, CancellationToken ct)
                        => Jint.JsValueExtensions.UnwrapIfPromise(engine.Invoke(fn), ct).ToObject();
                }
            }

            namespace Orkeon.Scripting.Internal
            {
                static class JsTrampolineFactories
                {
                    sealed class Factory
                    {
                        public static JsValue For(Engine engine) => engine.Evaluate("(x) => x");
                    }
                }
            }
            """;

        var violations = EngineThreadingAnalyzer.Analyze(CompileClean("Probe.cs", snippet));

        Assert.True(violations.Count == 0, "Unexpected violations:\n" + Report(violations));
    }

    private static string Report(IEnumerable<EngineThreadingViolation> violations)
        => string.Join("\n", violations.Select(v => "  " + v));

    /// <summary>The <c>// expect: rule</c> markers of a snippet, as (rule, 1-based line), in the order the assertion compares.</summary>
    private static (string Rule, int Line)[] ExpectedMarkers(string snippet)
        => snippet.Split('\n')
            .Select((text, index) => (Text: text, Line: index + 1))
            .Where(l => l.Text.Contains("// expect: ", StringComparison.Ordinal))
            .Select(l => (Rule: l.Text[(l.Text.IndexOf("// expect: ", StringComparison.Ordinal) + "// expect: ".Length)..].Trim(), l.Line))
            .OrderBy(v => v.Line).ThenBy(v => v.Rule)
            .ToArray();

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

    /// <summary>A control snippet must compile without errors: an unresolved receiver would fall back to the name and prove nothing about the semantic path.</summary>
    private static Compilation CompileClean(string fileName, string source)
    {
        var compilation = Compile([CSharpSyntaxTree.ParseText(source, ParseOptions, path: fileName)]);
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, "The control snippet does not compile:\n" + string.Join("\n", errors.Select(e => "  " + e)));
        return compilation;
    }

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
    /// Invoke) instead of falling back to the name alone. The scripting assembly itself is not
    /// referenced: the scanned sources are that assembly, and a metadata copy would let a dropped
    /// file's types resolve anyway.
    /// </summary>
    private static readonly Lazy<IReadOnlyList<MetadataReference>> References = new(() =>
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        foreach (var path in trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            if (File.Exists(path)) paths.Add(path);
        var scripting = typeof(ScriptHost).Assembly;
        foreach (var assembly in ReferenceClosure(scripting))
            if (!ReferenceEquals(assembly, scripting) && !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location)) paths.Add(assembly.Location);
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

    /// <summary>
    /// Build output lives in <c>bin/</c> and <c>obj/</c>, plus the per-OS intermediate folders
    /// <c>obj-*</c> (<c>obj-linux</c> here, per <c>Directory.Build.props</c>). Exact segments only:
    /// a prefix match once dropped <c>Bindings/</c>, where script-facing delegates are registered.
    /// </summary>
    private static bool IsBuildOutput(string project, string path)
        => Path.GetRelativePath(project, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
                            || segment.StartsWith("obj-", StringComparison.OrdinalIgnoreCase));
}

internal sealed record EngineThreadingViolation(string Rule, string File, int Line, string Detail)
{
    public override string ToString() => $"{File}:{Line} [{Rule}] {Detail}";
}

/// <summary>
/// The three rules over a compilation. Receiver types and containing types come from the
/// semantic model; when a symbol cannot be resolved the member name alone decides, on the
/// conservative side.
/// </summary>
internal static class EngineThreadingAnalyzer
{
    public const string EntryAfterAwait = "entry-after-await";
    public const string DrainOutsideRootPump = "drain-outside-root-pump";
    public const string EvaluateOutsideStore = "evaluate-outside-store";

    private static readonly HashSet<string> EntryNames = new(StringComparer.Ordinal) { "Invoke", "Evaluate", "FromObject", "SetValue", "Execute", "Call" };
    private static readonly HashSet<string> DrainNames = new(StringComparer.Ordinal) { "UnwrapIfPromise", "UnwrapIfPromiseAsync" };

    /// <summary>The root pumps, by fully qualified containing type and containing method declaration.</summary>
    private static readonly (string Type, string Member)[] RootPumps =
    [
        ("Orkeon.Scripting.ScriptHost", "EvaluateAsync"),
        ("Orkeon.Scripting.Runtime.JsCrew", "Pump"),
        ("Orkeon.Scripting.Runtime.JsTool", "CallAsync"),
    ];

    /// <summary>The types allowed to call <c>Engine.Evaluate</c>: the script's own root pump and the factory store.</summary>
    private static readonly string[] EvaluateOwners = ["Orkeon.Scripting.ScriptHost", "Orkeon.Scripting.Internal.JsTrampolineFactories"];

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
                    CheckEvaluateOwner(model, invocation, violations);
            }
        }
        return violations.OrderBy(v => v.File, StringComparer.Ordinal).ThenBy(v => v.Line).ToList();
    }

    // ---- rule 1: no engine entry after an await of the same async body ----

    /// <summary>
    /// A point of the body from which the rest runs on a continuation: <see cref="Position"/> is
    /// where the continuation starts in source order, <see cref="Span"/> the construct itself (a
    /// loop that contains it awaits on every iteration).
    /// </summary>
    private readonly record struct AwaitPoint(int Position, TextSpan Span);

    private static void CheckEntriesAfterAwait(SemanticModel model, SyntaxNode owner, List<EngineThreadingViolation> violations)
    {
        var body = BodyOf(owner);
        if (body is null) return;
        var nodes = body.DescendantNodesAndSelf(descendIntoChildren: n => ReferenceEquals(n, body) || !IsSeparateBody(model, n)).ToList();
        var awaits = nodes.Select(AwaitPointOf).Where(a => a.HasValue).Select(a => a!.Value).ToList();
        if (awaits.Count == 0) return;
        foreach (var invocation in nodes.OfType<InvocationExpressionSyntax>())
        {
            var name = InvokedName(invocation);
            if (name is null || !EntryNames.Contains(name) || !IsEngineEntry(model, invocation)) continue;
            // An await positioned anywhere before the entry's end counts: before it in source order,
            // or nested in its receiver or arguments (the call itself runs on the continuation). An
            // await whose operand contains the entry counts too, conservatively: the operand is
            // evaluated before the suspension, but `await Task.Run(() => engine.Invoke(...))` is an
            // entry from another thread, which the rule forbids just the same.
            var afterAwait = awaits.Any(a => a.Position < invocation.Span.End);
            var inAwaitingLoop = LoopsBetween(invocation, body).Any(loop => awaits.Any(a => loop.Span.Contains(a.Span)));
            if (afterAwait || inAwaitingLoop)
                violations.Add(Violation(EntryAfterAwait, invocation,
                    $"{name} after an await in {Describe(model, owner)}" + (inAwaitingLoop && !afterAwait ? " (inside a loop that awaits)" : "")));
        }
    }

    /// <summary>
    /// The await points: an <c>await</c> expression; an <c>await foreach</c>, whose body and
    /// everything after it follow the awaited <c>MoveNextAsync</c>; an <c>await using</c> block, whose
    /// <c>DisposeAsync</c> is awaited when the block ends; an <c>await using</c> declaration, disposed
    /// when its enclosing block ends.
    /// </summary>
    private static AwaitPoint? AwaitPointOf(SyntaxNode node) => node switch
    {
        AwaitExpressionSyntax a => new AwaitPoint(a.SpanStart, a.Span),
        CommonForEachStatementSyntax f when f.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword) => new AwaitPoint(f.SpanStart, f.Span),
        UsingStatementSyntax u when u.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword) => new AwaitPoint(u.Span.End, u.Span),
        LocalDeclarationStatementSyntax d when d.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword) => new AwaitPoint((d.Parent ?? d).Span.End, d.Span),
        _ => null,
    };

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
    private static bool IsSeparateBody(SemanticModel model, SyntaxNode node) => IsAsyncBody(node) || IsRootPumpLambda(model, node);

    private static bool IsRootPumpLambda(SemanticModel model, SyntaxNode node)
    {
        if (node is not AnonymousFunctionExpressionSyntax { Parent: ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax call } } } lambda)
            return false;
        if (lambda.Modifiers.Any(SyntaxKind.AsyncKeyword))
            return false;
        if (call.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Run", Expression: IdentifierNameSyntax { Identifier.ValueText: "Task" } })
            return false;
        return IsInRootPumpMember(model, node);
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
            if (current is ForStatementSyntax or CommonForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax)
                yield return current;
        }
    }

    // ---- rule 2: UnwrapIfPromise only in a root pump's synchronous body, on the CancellationToken overload ----

    private static void CheckDrain(SemanticModel model, InvocationExpressionSyntax invocation, string name, List<EngineThreadingViolation> violations)
    {
        if (!IsInRootPumpMember(model, invocation))
        {
            violations.Add(Violation(DrainOutsideRootPump, invocation, $"{name} outside the three root pumps ({Location(model, invocation)})"));
            return;
        }
        if (!IsInRootPumpSynchronousBody(model, invocation))
        {
            violations.Add(Violation(DrainOutsideRootPump, invocation,
                $"{name} in {Location(model, invocation)} but outside the pump's synchronous body: a drain on a continuation or from a nested function is a drain from a job"));
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

    /// <summary>
    /// The pump's synchronous body is the nearest function around the drain, and it is either the
    /// allow-listed method itself when that method is synchronous (<c>JsCrew.Pump</c>, dispatched by
    /// <c>Task.Run</c> from its caller) or the synchronous <c>Task.Run</c> lambda of the allow-listed
    /// method (<c>ScriptHost.EvaluateAsync</c>, <c>JsTool.CallAsync</c>). A nested async lambda, an
    /// async local function or any other nested function is not the pump.
    /// </summary>
    private static bool IsInRootPumpSynchronousBody(SemanticModel model, SyntaxNode node)
    {
        var context = node.Ancestors().FirstOrDefault(n => n is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or MethodDeclarationSyntax);
        return context switch
        {
            MethodDeclarationSyntax m => !m.Modifiers.Any(SyntaxKind.AsyncKeyword),
            AnonymousFunctionExpressionSyntax lambda => IsRootPumpLambda(model, lambda),
            _ => false,
        };
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

    private static void CheckEvaluateOwner(SemanticModel model, InvocationExpressionSyntax invocation, List<EngineThreadingViolation> violations)
    {
        // Any enclosing type counts: the store keeps its per-factory state in a nested class.
        if (EnclosingTypeNames(model, invocation).Any(name => EvaluateOwners.Contains(name, StringComparer.Ordinal))) return;
        violations.Add(Violation(EvaluateOutsideStore, invocation, $"Engine.Evaluate in {ContainingTypeName(model, invocation) ?? "<no type>"}: a trampoline factory belongs in JsTrampolineFactories"));
    }

    // ---- shared ----

    /// <summary>Whether the node sits in an allow-listed pump member: fully qualified containing type plus nearest method declaration.</summary>
    private static bool IsInRootPumpMember(SemanticModel model, SyntaxNode node)
    {
        var type = ContainingTypeName(model, node);
        var member = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;
        return type is not null && member is not null
            && RootPumps.Any(p => string.Equals(p.Type, type, StringComparison.Ordinal) && string.Equals(p.Member, member, StringComparison.Ordinal));
    }

    /// <summary>The fully qualified names of the enclosing types, innermost first; the identifier alone when the declaration has no symbol.</summary>
    private static IEnumerable<string> EnclosingTypeNames(SemanticModel model, SyntaxNode node)
        => node.Ancestors().OfType<TypeDeclarationSyntax>()
            .Select(t => model.GetDeclaredSymbol(t)?.ToDisplayString() ?? t.Identifier.ValueText);

    private static string? ContainingTypeName(SemanticModel model, SyntaxNode node) => EnclosingTypeNames(model, node).FirstOrDefault();

    private static string Location(SemanticModel model, SyntaxNode node)
    {
        var type = ContainingTypeName(model, node) ?? "<no type>";
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

    private static string Describe(SemanticModel model, SyntaxNode owner) => owner switch
    {
        MethodDeclarationSyntax m => "async method " + m.Identifier.ValueText,
        LocalFunctionStatementSyntax f => "async local function " + f.Identifier.ValueText,
        _ => "an async lambda of " + Location(model, owner),
    };

    private static EngineThreadingViolation Violation(string rule, SyntaxNode node, string detail)
    {
        var span = node.GetLocation().GetLineSpan();
        return new EngineThreadingViolation(rule, span.Path, span.StartLinePosition.Line + 1, detail);
    }
}
