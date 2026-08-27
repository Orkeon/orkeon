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
        var declarations = new Dictionary<string, List<string>>(StringComparer.Ordinal);

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

                kinds.Add($"{kind} in {Path.GetFileName(file)}");
            }
        }

        var conflicts = declarations
            .Where(d => d.Value.Count > 1)
            .Select(d => $"'{d.Key}' declared as {string.Join(" and ", d.Value)}")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(conflicts);
    }

    [System.Text.RegularExpressions.GeneratedRegex(
        @"^\s*(export\s+)?(declare\s+)?(?<kind>interface|namespace|type|const|function|class|enum)\s+(?<name>[A-Za-z_$][A-Za-z0-9_$]*)")]
    private static partial System.Text.RegularExpressions.Regex DeclarationPattern();

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
