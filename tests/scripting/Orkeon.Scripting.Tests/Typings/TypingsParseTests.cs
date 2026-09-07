using Orkeon.Scripting.Toolchain;

namespace Orkeon.Scripting.Tests.Typings;

/// <summary>
/// The shipped <c>.d.ts</c> files are handed to esbuild, the same TypeScript front end that
/// reads every user script.
/// <para>
/// The suite next door asserts that certain substrings appear in the rolled-up bundle. That
/// cannot fail when the typings stop being TypeScript — and one of them had not been for a
/// while: <c>tools.d.ts</c> declared an index signature as <c>const [name: string]: …</c>,
/// which a namespace cannot carry and which is not a declaration at all. The file did not
/// parse, so the <c>tools</c> namespace this package advertises was unavailable to every
/// editor that loaded it, with a green test suite and every substring present.
/// </para>
/// <para>
/// <b>What esbuild can and cannot say.</b> It is a transpiler: it strips types and reports
/// syntax errors only. It never type-checks, so it is silent on the errors an editor shows
/// first — a duplicate identifier, a merged declaration whose modifiers disagree. The
/// replacement for that unparseable index signature was a <c>namespace tools</c> beside a
/// <c>const tools</c>, which do not merge (TS2300/TS2395); a second <c>LlmConfig</c> in
/// <c>llm.d.ts</c> collided with the one in <c>agent.d.ts</c> (TS2687 ×6, TS2717). Both
/// passed the theory below, because both are syntactically fine. Hence
/// <see cref="No_global_name_is_declared_twice_in_a_conflicting_shape"/>, which checks the
/// one property esbuild structurally cannot.
/// </para>
/// </summary>
public sealed partial class TypingsParseTests
{
    public static TheoryData<string> TypingFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(TypingsDirectory(), "*.d.ts").Order(StringComparer.Ordinal))
            data.Add(Path.GetFileName(file));

        return data;
    }

    /// <summary>
    /// Parses. Not "is valid TypeScript" — see the class remarks for what esbuild cannot say.
    /// </summary>
    [Theory]
    [MemberData(nameof(TypingFiles))]
    public async Task Every_shipped_typing_parses_as_typescript(string fileName)
    {
        var source = await File.ReadAllTextAsync(
            Path.Combine(TypingsDirectory(), fileName), TestContext.Current.CancellationToken);

        using var transpiler = new EsbuildTranspiler();

        try
        {
            // The output is discarded: a .d.ts strips to nothing. Parsing is the assertion.
            await transpiler.TranspileAsync(source, TestContext.Current.CancellationToken);
        }
        catch (EsbuildNotFoundException)
        {
            Assert.Skip("esbuild is not available in this environment.");
        }
        catch (EsbuildTranspileException ex)
        {
            Assert.Fail($"{fileName} is not valid TypeScript:\n{ex.Message}");
        }
    }

    /// <summary>
    /// Across all the <c>declare global</c> blocks the package ships, a given name is
    /// introduced by at most one <c>namespace</c>/<c>const</c>/<c>type</c>, and an
    /// <c>interface</c> name is not also taken by one of those.
    /// <para>
    /// Interfaces of the same name DO merge in TypeScript — which is why a second
    /// <c>LlmConfig</c> was not a syntax error but a type error on every member whose
    /// modifiers differed. A namespace and a const do not merge at all. Both shapes are
    /// invisible to a transpiler and immediate in an editor, which is the audience these
    /// files exist for.
    /// </para>
    /// </summary>
    [Fact]
    public void No_global_name_is_declared_twice_in_a_conflicting_shape()
    {
        var declarations = new Dictionary<string, List<(string Kind, string File)>>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(TypingsDirectory(), "*.d.ts").Order(StringComparer.Ordinal))
        {
            foreach (var line in File.ReadLines(file))
            {
                var match = DeclarationPattern().Match(line);
                if (!match.Success)
                    continue;

                var name = match.Groups["name"].Value;
                var kind = match.Groups["kind"].Value;
                if (!declarations.TryGetValue(name, out var kinds))
                    declarations[name] = kinds = [];

                kinds.Add((kind, Path.GetFileName(file)));
            }
        }

        var conflicts = declarations
            .Where(d => d.Value.Count > 1 && !IsTypeAndValueCompanion(d.Value))
            .Select(d => $"'{d.Key}' declared as {string.Join(" and ", d.Value.Select(k => $"{k.Kind} in {k.File}"))}")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(conflicts);
    }

    /// <summary>
    /// One name in type space and the same name in value space is not a duplicate — it is how
    /// TypeScript declares a type together with the factory that builds it, the way the lib
    /// files pair <c>Promise</c> the type with <c>Promise</c> the constructor. <c>ErrorAction</c>
    /// is ours: the interface an <c>onError</c> handler returns, and the const that produces one.
    /// The shapes this test exists for — two interfaces whose members disagree, a namespace
    /// against a const — stay conflicts, because neither pair merges.
    /// </summary>
    private static bool IsTypeAndValueCompanion(List<(string Kind, string File)> kinds)
        => kinds.Count == 2
           && kinds.Count(k => k.Kind == "interface") == 1
           && kinds.Count(k => k.Kind is "const" or "function") == 1
           && kinds.Select(k => k.File).Distinct(StringComparer.Ordinal).Count() == 1;

    [System.Text.RegularExpressions.GeneratedRegex(
        @"^\s*(export\s+)?(declare\s+)?(?<kind>interface|namespace|type|const|function|class|enum)\s+(?<name>[A-Za-z_$][A-Za-z0-9_$]*)")]
    private static partial System.Text.RegularExpressions.Regex DeclarationPattern();

    /// <summary>
    /// Every global the engine registers must be declared in the shipped typings. Nothing
    /// enforced this, and the gap is what let <c>ErrorAction</c> drift: the binding registered
    /// a factory while <c>agent.d.ts</c> declared a union of object literals, so a handler
    /// written against the published typings compiled and then silently failed the run.
    ///
    /// <para>
    /// The names come from the bindings themselves, so adding a binding without declaring it
    /// fails here rather than reaching a user's editor.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("agentBuilder")]
    [InlineData("crewBuilder")]
    [InlineData("taskBuilder")]
    [InlineData("toolBuilder")]
    [InlineData("ErrorAction")]
    [InlineData("llm")]
    [InlineData("rag")]
    [InlineData("tools")]
    [InlineData("stateMachine")]
    [InlineData("stateGraph")]
    public void Every_registered_global_is_declared_in_the_typings(string globalName)
    {
        var declared = Directory.EnumerateFiles(TypingsDirectory(), "*.d.ts")
            .SelectMany(File.ReadLines)
            .Select(line => DeclarationPattern().Match(line))
            .Where(m => m.Success)
            .Select(m => m.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(globalName, declared);
    }

    [Fact]
    public void The_typings_directory_is_not_empty()
    {
        // Guards the guard: an empty theory source passes silently.
        Assert.NotEmpty(Directory.EnumerateFiles(TypingsDirectory(), "*.d.ts"));
    }

    private static string TypingsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName, "src", "scripting", "Orkeon.Scripting", "Typings");
            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate src/scripting/Orkeon.Scripting/Typings above {AppContext.BaseDirectory}.");
    }
}
