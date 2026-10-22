using System.Diagnostics;
using System.Runtime.CompilerServices;
using Orkeon.Scripting.Bindings;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// #125 - <c>Typings/tools.generated.d.ts</c> is rendered from the tools a run registers, and a script
/// that calls them type-checks against it: the input of every built-in tool is its request record's,
/// so a parameter renamed on the .NET side fails <c>tsc</c> instead of the run.
/// <para>
/// The committed file is the one rendered here; a tool change regenerates it with
/// <c>ORKEON_REGENERATE_TYPINGS=1</c> (the command is in the file's header). The type checks need the
/// TypeScript compiler of <c>tools/scripting-typecheck</c> (<c>npm --prefix tools/scripting-typecheck ci</c>)
/// and are skipped without it, as the esbuild checks are without esbuild.
/// </para>
/// Joins <see cref="ConsoleSerialCollection"/> because the host build writes on the console.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class BuiltInToolTypingsTests : IDisposable
{
    /// <summary>
    /// The 17 built-in tools Orkeon Code calls, each with the inputs its contract declares: what a
    /// product kept in a copy of its own, which drifted silently, before these declarations existed.
    /// </summary>
    private const string CallsOfOrkeonCode = """
        async function main(): Promise<void> {
            await tools.fileRead({ path: "/workspace/README.md", max_length: 4000 });
            await tools.fileWrite({ path: "/output/notes.md", content: "notes" });
            await tools.directoryRead({ path: "/workspace", max_results: 50 });
            await tools.shellCommand({ command: "dotnet --info" });
            await tools.sessionStore({ operation: "append_note", note: "hello" });
            await tools.sessionStore({ operation: "set_state", key: "k", value: null });
            await tools.sessionSnip({ retain_count: 3 });
            await tools.sessionStats();
            await tools.sessionCost();
            await tools.tokenBudget();
            await tools.memoryStore({ operation: "add", category: "plan", content: "ship it" });
            await tools.progressReport({ label: "Indexing", percent: 40 });
            await tools.listMounts();
            await tools.indexStatus();
            await tools.indexCodebase({ root_path: "/workspace" });
            await tools.codebaseMap({ max_entries: 50 });
            await tools.dependencyGraph({ max_nodes: 100 });
            await tools.packageSummary({ fqn: "Orkeon" });
        }
        void main;
        """;

    private readonly string _root;

    public BuiltInToolTypingsTests() => _root = RegisteredTools.CreateRoot("orkeon-tool-typings-");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    [Fact]
    public async Task ShouldMatchTheCommittedDeclarations_WhenRenderedFromTheRegisteredTools()
    {
        var rendered = BuiltInToolTypings.Render(await RegisteredTools.ReadAsync(_root));
        var committed = Path.Combine(TypingsDirectory(), BuiltInToolTypings.FileName);

        if (Environment.GetEnvironmentVariable("ORKEON_REGENERATE_TYPINGS") == "1")
            await File.WriteAllTextAsync(committed, rendered, TestContext.Current.CancellationToken);

        var onDisk = (await File.ReadAllTextAsync(committed, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");
        Assert.True(
            rendered == onDisk,
            $"{BuiltInToolTypings.FileName} is stale: a built-in tool changed its inputs. Regenerate it with the command in its header.");
    }

    [Fact]
    public async Task ShouldExposeTheReadLimitsAndTheNullableMembersTheToolsRead()
    {
        var rendered = BuiltInToolTypings.Render(await RegisteredTools.ReadAsync(_root));

        Assert.Matches(@"fileRead\(input: \{[^}]*\bmax_length\?: number \| null;", rendered);
        Assert.Matches(@"directoryRead\(input: \{[^}]*\bmax_results\?: number;", rendered);
        // An optional nullable member takes null: set_state with a null value removes the key. A required
        // one does not, nullable or not: web_scrape refuses a null url ("URL cannot be empty").
        Assert.Contains("value?: string | null;", rendered, StringComparison.Ordinal);
        Assert.Matches(@"webScrape\(input: \{[^}]*\burl: string;", rendered);
    }

    [Fact]
    public async Task ShouldTypeCheck_WhenAScriptCallsTheToolsWithTheInputsTheirContractsDeclare()
    {
        var (exit, output) = await TypeCheckAsync(CallsOfOrkeonCode);

        Assert.True(exit == 0, "tsc refused the calls:\n" + output);
    }

    [Fact]
    public async Task ShouldFailTheTypeCheck_WhenAParameterIsMisspelled()
    {
        var misspelled = CallsOfOrkeonCode.Replace("max_length: 4000", "max_lenght: 4000", StringComparison.Ordinal);

        var (exit, output) = await TypeCheckAsync(misspelled);

        Assert.NotEqual(0, exit);
        Assert.Contains("max_lenght", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Runs <c>tsc</c> over the shipped typings - <c>tools.generated.d.ts</c> rendered afresh from the
    /// registered tools - and <paramref name="script"/>, with the options an author copies.
    /// </summary>
    private async Task<(int Exit, string Output)> TypeCheckAsync(string script)
    {
        var toolDirectory = Path.Combine(RepositoryRoot(), "tools", "scripting-typecheck");
        var tsc = Path.Combine(toolDirectory, "node_modules", "typescript", "bin", "tsc");
        Assert.SkipWhen(!File.Exists(tsc), "tsc is not bootstrapped: npm --prefix tools/scripting-typecheck ci");
        Assert.SkipWhen(FindOnPath("node") is null, "node is not available in this environment.");

        var work = Path.Combine(_root, "typecheck");
        Directory.CreateDirectory(work);
        var files = new List<string>();
        foreach (var source in Directory.EnumerateFiles(TypingsDirectory(), "*.d.ts").Order(StringComparer.Ordinal))
        {
            var copy = Path.Combine(work, Path.GetFileName(source));
            File.Copy(source, copy);
            files.Add(copy);
        }
        await File.WriteAllTextAsync(
            Path.Combine(work, BuiltInToolTypings.FileName),
            BuiltInToolTypings.Render(await RegisteredTools.ReadAsync(_root)),
            TestContext.Current.CancellationToken);
        var scriptPath = Path.Combine(work, "calls.ts");
        await File.WriteAllTextAsync(scriptPath, script, TestContext.Current.CancellationToken);
        files.Add(scriptPath);

        var project = Path.Combine(work, "tsconfig.json");
        await File.WriteAllTextAsync(
            project,
            System.Text.Json.JsonSerializer.Serialize(new { extends = Path.Combine(toolDirectory, "tsconfig.base.json"), files }),
            TestContext.Current.CancellationToken);

        var start = new ProcessStartInfo("node", [tsc, "--project", project])
        {
            WorkingDirectory = work,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return (process.ExitCode, await stdout + await stderr);
    }

    [Fact]
    [Trait("BR", "RT-27")]
    public async Task ShouldTypeListMountsResponse_WhenTypingsGenerated()
    {
        var rendered = BuiltInToolTypings.Render(await RegisteredTools.ReadAsync(_root));

        var line = rendered.ReplaceLineEndings("\n").Split('\n')
            .Single(l => l.Contains("listMounts(", StringComparison.Ordinal));
        Assert.DoesNotContain("Promise<unknown>", line, StringComparison.Ordinal);
        Assert.Contains("mounts", rendered, StringComparison.Ordinal);
        Assert.Contains("virtual_path: string;", rendered, StringComparison.Ordinal);
        Assert.Contains("access: string;", rendered, StringComparison.Ordinal);
    }

    private static string? FindOnPath(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(d => new[] { Path.Combine(d, program), Path.Combine(d, program + ".exe") })
            .FirstOrDefault(File.Exists);

    private static string TypingsDirectory() =>
        Path.Combine(RepositoryRoot(), "src", "scripting", "Orkeon.Scripting", "Typings");

    private static string RepositoryRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
}
