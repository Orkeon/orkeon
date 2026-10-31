using System.Diagnostics;

namespace Orkeon.Scripting.Tests.Typings;

/// <summary>
/// Type-checks small scripts against the shipped typings with the repository's tsc
/// (tools/scripting-typecheck), as scripts/check-scripting-typings.sh does.
/// <para>
/// A fast test: CI's fast suite runs after that script has bootstrapped tsc. Without tsc or
/// node the checks are skipped, as BuiltInToolTypingsTests does, never failed: the nightly
/// Integration and Slow run bootstraps no npm package (#236).
/// </para>
/// </summary>
public sealed class DecisionTypingsTests
{
    private const string DecisionScript = """
        const triage = taskBuilder().name("triage").decision({
            questions: {
                category: { choice: { bug: "a defect" } },
                urgency: { score: ["low", "high"], instructions: "x" },
                refund: { noul: "asks for a refund" },
            },
            minConfidence: 0.5,
            state: ["intake"],
            longContext: true,
            fallback: { category: "bug" },
        });
        const refund = taskBuilder().name("refund").when("triage", { category: ["billing"], urgency: ">= 0.7" });
        const other = taskBuilder().name("other").when("triage", "otherwise");
        """;

    private const string CrewScript = """
        const triager = agentBuilder().name("triager").role("Triage").goal("Sort tickets").build();
        const triage = taskBuilder().name("triage").decision({
            questions: { category: { choice: { bug: "a defect", billing: "money" } } },
            state: ["intake"],
            fallback: { category: "bug" },
        });
        const intake = taskBuilder().name("intake");
        const refund = taskBuilder().name("refund").when("triage", { category: ["billing"] });
        const other = taskBuilder().name("other").when("triage", "otherwise");
        const crew = crewBuilder().name("desk").withAgent(triager).build();
        """;

    [Fact]
    [Trait("BR", "R-33")]
    [Trait("BR", "R-34")]
    public async Task ShouldTypeCheck_WhenCrewIsBuiltWithTasksAndDecision()
    {
        var inline = await TypeCheckAsync(CrewScript);
        Assert.Equal(0, inline.ExitCode);
        Assert.Equal(string.Empty, inline.Output);

        var example = FindUp(Path.Combine("examples", "scripting", "09-decision-and-when.ork.ts"));
        Assert.True(example is not null, "examples/scripting/09-decision-and-when.ork.ts is missing.");
        var shipped = await TypeCheckAsync(await File.ReadAllTextAsync(example!, TestContext.Current.CancellationToken));
        Assert.Equal(0, shipped.ExitCode);
        Assert.Equal(string.Empty, shipped.Output);
    }

    [Fact]
    [Trait("BR", "R-33")]
    public async Task ShouldFailTypeCheck_WhenDecisionHasWrongFallbackType()
    {
        var script = CrewScript.Replace("fallback: { category: \"bug\" }", "fallback: { bug: 3 }", StringComparison.Ordinal);

        var result = await TypeCheckAsync(script);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("TS2322", result.Output, StringComparison.Ordinal);
        Assert.Contains("'number' is not assignable to type 'string'", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "UC-01")]
    public async Task ShouldRejectTypeCheck_WhenMinConfidenceIsNotANumber()
    {
        var script = DecisionScript.Replace("minConfidence: 0.5", "minConfidence: \"high\"", StringComparison.Ordinal);
        var expectedLine = script.Split('\n').ToList().FindIndex(l => l.Contains("minConfidence: \"high\"", StringComparison.Ordinal)) + 1;

        var diagnostics = (await TypeCheckAsync(script)).Output;

        Assert.Contains("TS2322", diagnostics, StringComparison.Ordinal);
        Assert.Contains("'string' is not assignable to type 'number'", diagnostics, StringComparison.Ordinal);
        Assert.Matches($@"script\.ts\({expectedLine},\d+\): error TS2322", diagnostics);
    }

    private static string? FindUp(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate) || Directory.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static string? FindOnPath(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(d => new[] { Path.Combine(d, program), Path.Combine(d, program + ".exe") })
            .FirstOrDefault(File.Exists);

    private static async Task<(int ExitCode, string Output)> TypeCheckAsync(string script)
    {
        var typings = FindUp(Path.Combine("src", "scripting", "Orkeon.Scripting", "Typings"));
        var tsc = FindUp(Path.Combine("tools", "scripting-typecheck", "node_modules", "typescript", "bin", "tsc"));
        Assert.True(typings is not null, "Typings folder not found above the test output.");
        Assert.SkipWhen(tsc is null, "tsc is not bootstrapped: npm --prefix tools/scripting-typecheck ci");
        Assert.SkipWhen(FindOnPath("node") is null, "node is not available in this environment.");

        var work = Path.Combine(Path.GetTempPath(), "orkeon-decision-typings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var files = new List<string>();
            foreach (var d in Directory.EnumerateFiles(typings!, "*.d.ts"))
            {
                var target = Path.Combine(work, Path.GetFileName(d));
                File.Copy(d, target);
                files.Add(target);
            }
            var scriptPath = Path.Combine(work, "script.ts");
            await File.WriteAllTextAsync(scriptPath, script, TestContext.Current.CancellationToken);
            files.Add(scriptPath);

            var psi = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add(tsc!);
            foreach (var a in new[] { "--noEmit", "--strict", "--target", "ES2022", "--lib", "ES2022", "--module", "ESNext",
                "--moduleResolution", "bundler", "--moduleDetection", "force", "--pretty", "false" })
                psi.ArgumentList.Add(a);
            foreach (var f in files)
                psi.ArgumentList.Add(f);

            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            return (process.ExitCode, ((await stdout) + (await stderr)).Trim());
        }
        finally
        {
            Directory.Delete(work, true);
        }
    }
}
