using System.Text;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Tools.FileSystem.Tests;

/// <summary>
/// Regression coverage for Experiment 07 friction #6: <c>count_pattern</c> must support
/// distinct-match counting and match enumeration so verifier tasks can answer
/// "how many unique slugs are emitted?" and "list them" without a second tool round-trip.
/// </summary>
public sealed class CountPatternDistinctAndMatchesTests : IDisposable
{
    private static readonly string[] ExpectedOrderedMatches = ["TBD_a", "TBD_b", "TBD_c"];
    private static readonly string[] ExpectedSortedUniqueMatches = ["TBD_alpha", "TBD_bravo", "TBD_charlie"];

    private readonly CountPatternTool _tool = new(new PassThroughFileSystemService());
    private readonly string _testDir;

    public CountPatternDistinctAndMatchesTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "countpattern-distinct-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        _tool.Dispose();
        try { Directory.Delete(_testDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private string WriteText(string relativePath, string content)
    {
        var fullPath = Path.Combine(_testDir, relativePath);
        File.WriteAllText(fullPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return fullPath;
    }

    private async Task<CountPatternResponse> RunAsync(
        string filePath,
        string pattern,
        bool distinct = false,
        bool includeMatches = false,
        int? maxMatches = null)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["path"] = filePath,
            ["patterns"] = new[] { pattern },
            ["multiline"] = true,
            ["distinct_matches"] = distinct,
            ["include_matches"] = includeMatches,
        };
        if (maxMatches.HasValue) parameters["max_matches_returned"] = maxMatches.Value;

        var response = await _tool.CallAsync(new ToolCallRequest("count_pattern", parameters));
        Assert.True(response.Success, response.Error ?? "Expected success");
        var dict = response.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        var json = System.Text.Json.JsonSerializer.Serialize(dict);
        var typed = System.Text.Json.JsonSerializer.Deserialize<CountPatternResponse>(json);
        Assert.NotNull(typed);
        return typed!;
    }

    [Fact]
    public async Task DistinctMatches_True_ReturnsCardinalityOfUniqueMatches()
    {
        // The same slug appears 5 times across the document — once would be enough.
        var path = WriteText("repeats.md", string.Join('\n', Enumerable.Repeat("TBD_command_registry", 5)));

        var distinct = await RunAsync(path, "TBD_[a-z_]+", distinct: true);
        var raw = await RunAsync(path, "TBD_[a-z_]+", distinct: false);

        Assert.Equal(1, distinct.Counts[0].Count);
        Assert.Equal(5, raw.Counts[0].Count);
    }

    [Fact]
    public async Task DistinctMatches_True_WithOptionalPrefix_ReportsTwoUniqueSlugs()
    {
        // Mixed `# TBD_x` (section anchor) and `TBD_y` (table cell). The recommended pattern
        // is `(?:# )?TBD_[a-z_]+`; with DistinctMatches it should count two unique slugs
        // (the `# ` prefix differentiates the captures in raw mode, but the *contents* are
        // distinct strings).
        var path = WriteText("mixed.md", "# TBD_alpha\nrow uses TBD_beta in section 7\nlater # TBD_alpha\n");

        var distinct = await RunAsync(path, @"(?:# )?TBD_[a-z_]+", distinct: true);

        // Three captures: "# TBD_alpha", "TBD_beta", "# TBD_alpha" → 2 unique strings.
        Assert.Equal(2, distinct.Counts[0].Count);
    }

    [Fact]
    public async Task IncludeMatches_True_PopulatesMatchedSubstrings()
    {
        var path = WriteText("list.md", "TBD_a\nTBD_b\nTBD_c\n");

        var response = await RunAsync(path, "TBD_[a-z]", includeMatches: true);

        var entry = response.Counts[0];
        Assert.Equal(3, entry.Count);
        Assert.NotNull(entry.Matches);
        Assert.Equal(ExpectedOrderedMatches, entry.Matches!);
    }

    [Fact]
    public async Task IncludeMatches_True_WithDistinctMatches_ReturnsSortedUnique()
    {
        var path = WriteText("scrambled.md", "TBD_charlie\nTBD_alpha\nTBD_charlie\nTBD_bravo\n");

        var response = await RunAsync(path, "TBD_[a-z]+", distinct: true, includeMatches: true);

        var entry = response.Counts[0];
        Assert.Equal(3, entry.Count);
        Assert.Equal(ExpectedSortedUniqueMatches, entry.Matches!);
    }

    [Fact]
    public async Task IncludeMatches_True_HonorsMaxMatchesReturned()
    {
        var path = WriteText("dense.md",
            string.Join('\n', Enumerable.Range(0, 50).Select(i => $"TBD_slug_{i:000}")));

        var response = await RunAsync(path, "TBD_slug_[0-9]+", includeMatches: true, maxMatches: 10);

        var entry = response.Counts[0];
        Assert.Equal(50, entry.Count);
        Assert.NotNull(entry.Matches);
        Assert.Equal(10, entry.Matches!.Count);
        Assert.Equal("TBD_slug_000", entry.Matches[0]);
        Assert.Equal("TBD_slug_009", entry.Matches[9]);
    }

    [Fact]
    public async Task DefaultMaxMatchesReturned_Caps_At_200()
    {
        var path = WriteText("very-dense.md",
            string.Join('\n', Enumerable.Range(0, 500).Select(i => $"X{i}")));

        var response = await RunAsync(path, @"X\d+", includeMatches: true);

        var entry = response.Counts[0];
        Assert.Equal(500, entry.Count);
        Assert.NotNull(entry.Matches);
        Assert.Equal(200, entry.Matches!.Count);
    }

    [Fact]
    public async Task BackwardCompat_NoFlags_ReturnsRawCount_AndNullMatches()
    {
        var path = WriteText("plain.md", "TBD_a\nTBD_b\nTBD_a\n");

        var response = await RunAsync(path, "TBD_[a-z]");

        var entry = response.Counts[0];
        Assert.Equal(3, entry.Count);
        Assert.Null(entry.Matches);
    }
}
