using System.Text;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Tools.FileSystem.Tests;

/// <summary>
/// Tests for <see cref="CountPatternTool"/>.
///
/// R30-P1 regression coverage: a YAML deliverable starting with a literal
/// `name:` at byte 0 must match the canonical first-line shape gate
/// `\A(name:|...|# )`. R29 attempt-06 D shipped count=0 on such a file,
/// likely due to a U+FEFF code point surviving in the decoded string.
/// </summary>
public sealed class CountPatternToolTests : IDisposable
{
    private static readonly string[] UnterminatedPattern = ["[unterminated"];

    private readonly CountPatternTool _tool;
    private readonly string _testDir;

    public CountPatternToolTests()
    {
        _tool = new CountPatternTool(new PassThroughFileSystemService());
        _testDir = Path.Combine(Path.GetTempPath(), $"countpattern_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        _tool.Dispose();
        try { Directory.Delete(_testDir, recursive: true); } catch { /* swallow */ }
        GC.SuppressFinalize(this);
    }

    // -- Helpers ────────────────────────────────────────────────────────────

    private string WriteRaw(string relativePath, byte[] bytes)
    {
        var fullPath = Path.Combine(_testDir, relativePath);
        File.WriteAllBytes(fullPath, bytes);
        return fullPath;
    }

    private string WriteText(string relativePath, string content, Encoding? encoding = null)
    {
        var fullPath = Path.Combine(_testDir, relativePath);
        File.WriteAllText(fullPath, content, encoding ?? new UTF8Encoding(false));
        return fullPath;
    }

    private async Task<CountPatternResponse> Run(string filePath, params string[] patterns)
    {
        var request = new ToolCallRequest(
            ToolName: "count_pattern",
            Parameters: new Dictionary<string, object?>
            {
                ["path"] = filePath,
                ["patterns"] = patterns,
                ["multiline"] = true,
            });
        var response = await _tool.CallAsync(request);
        Assert.True(response.Success, response.Error ?? "Expected success");
        var dict = response.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        // The tool returns the typed response; recover a CountPatternResponse
        // from the dictionary representation via re-serialisation.
        var json = System.Text.Json.JsonSerializer.Serialize(dict);
        var typed = System.Text.Json.JsonSerializer.Deserialize<CountPatternResponse>(json);
        Assert.NotNull(typed);
        return typed!;
    }

    private static int CountFor(CountPatternResponse response, string pattern)
    {
        var entry = response.Counts.FirstOrDefault(c => c.Pattern == pattern);
        Assert.NotNull(entry);
        Assert.Null(entry!.Error);
        return entry.Count;
    }

    // -- Baseline ───────────────────────────────────────────────────────────

    [Fact]
    public async Task BackslashA_AnchoredAlternation_MatchesFirstAlternative()
    {
        var path = WriteText("clean.yaml",
            "name: orkeon_demo_resolved\r\ngoal: >\r\n  do something\r\n");
        var result = await Run(path,
            @"\A(name:|goal:|process:|resolution_meta:|# )");
        Assert.Equal(1, CountFor(result, @"\A(name:|goal:|process:|resolution_meta:|# )"));
        Assert.False(result.BomStripped);
    }

    [Fact]
    public async Task CaretAnchored_NameLiteral_MatchesFirstLine()
    {
        var path = WriteText("clean.yaml",
            "name: orkeon_demo_resolved\nbody\n");
        var result = await Run(path, @"^name:");
        Assert.Equal(1, CountFor(result, @"^name:"));
    }

    // -- BOM-related regressions (R30-P1) ───────────────────────────────────

    [Fact]
    public async Task LiteralFEFFCodePoint_AtHead_IsStripped_AnchorMatches()
    {
        // Simulate the surviving-BOM-in-string scenario that broke R29
        // attempt-06 D: write a doubled UTF-8 BOM so that StreamReader's
        // default BOM detection consumes the first three bytes (EF BB BF)
        // and the second copy decodes as a literal U+FEFF code point at
        // the head of the string returned by File.ReadAllTextAsync.
        var bytes = new List<byte>();
        bytes.AddRange([0xEF, 0xBB, 0xBF]);
        bytes.AddRange(Encoding.UTF8.GetBytes("﻿name: orkeon_demo_resolved\nbody\n"));
        var path = WriteRaw("bom_in_string.yaml", bytes.ToArray());

        // First, read the file directly to confirm the U+FEFF is present.
        var raw = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal('﻿', raw[0]);

        var result = await Run(path,
            @"\A(name:|goal:|# )",
            @"^name:");
        Assert.True(result.BomStripped,
            "Expected the tool to report bom_stripped=true when leading U+FEFF was removed");
        Assert.Equal(1, CountFor(result, @"\A(name:|goal:|# )"));
        Assert.Equal(1, CountFor(result, @"^name:"));
    }

    [Fact]
    public async Task ProperUtf8Bom_ConsumedByDecoder_NoStripNeeded()
    {
        // A real UTF-8 BOM byte sequence (EF BB BF) at file head IS consumed
        // by File.ReadAllTextAsync's BOM detection, so the decoded string
        // starts directly with `name:`. The tool should report
        // bom_stripped=false in that case.
        var bytes = new List<byte> { 0xEF, 0xBB, 0xBF };
        bytes.AddRange(Encoding.UTF8.GetBytes("name: orkeon_demo_resolved\nbody\n"));
        var path = WriteRaw("real_bom.yaml", bytes.ToArray());

        var result = await Run(path, @"\Aname:");
        Assert.False(result.BomStripped);
        Assert.Equal(1, CountFor(result, @"\Aname:"));
    }

    // -- Line-ending robustness ─────────────────────────────────────────────

    [Fact]
    public async Task CrlfFile_AnchorMatchesFirstLine()
    {
        var path = WriteText("crlf.yaml",
            "name: orkeon_demo_resolved\r\ngoal: >\r\n  do something\r\n");
        var result = await Run(path, @"\Aname:");
        Assert.Equal(1, CountFor(result, @"\Aname:"));
    }

    [Fact]
    public async Task LfFile_AnchorMatchesFirstLine()
    {
        var path = WriteText("lf.yaml",
            "name: orkeon_demo_resolved\ngoal: >\n  do something\n");
        var result = await Run(path, @"\Aname:");
        Assert.Equal(1, CountFor(result, @"\Aname:"));
    }

    // -- Multiline option semantics ─────────────────────────────────────────

    [Fact]
    public async Task Multiline_True_CaretMatchesEachLineStart()
    {
        var path = WriteText("multi.md",
            "# heading 1\nbody\n# heading 2\nbody\n");
        var result = await Run(path, @"^# ");
        Assert.Equal(2, CountFor(result, @"^# "));
    }

    [Fact]
    public async Task NumberOfMatches_NonOverlapping()
    {
        var path = WriteText("counts.txt", "abab abab abab");
        var result = await Run(path, @"ab");
        Assert.Equal(6, CountFor(result, @"ab"));
    }

    // -- Error handling ─────────────────────────────────────────────────────

    [Fact]
    public async Task InvalidRegex_PopulatesErrorField()
    {
        var path = WriteText("any.txt", "content");
        var request = new ToolCallRequest(
            ToolName: "count_pattern",
            Parameters: new Dictionary<string, object?>
            {
                ["path"] = path,
                ["patterns"] = UnterminatedPattern,
            });
        var response = await _tool.CallAsync(request, TestContext.Current.CancellationToken);
        Assert.True(response.Success);
        var dict = response.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        var json = System.Text.Json.JsonSerializer.Serialize(dict);
        var typed = System.Text.Json.JsonSerializer.Deserialize<CountPatternResponse>(json);
        Assert.NotNull(typed);
        Assert.Single(typed!.Counts);
        Assert.NotNull(typed.Counts[0].Error);
        Assert.Equal(0, typed.Counts[0].Count);
    }
}
