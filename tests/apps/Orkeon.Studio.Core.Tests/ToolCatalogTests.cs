using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Tools;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// STUDIO-21 — the tool catalogue the Settings screen lists is pinned against the documented
/// inventory: every name it carries is a real registry name, and it carries exactly the tools
/// the <c>orkeon run</c> column of the availability matrix says a run exposes.
/// </summary>
public sealed partial class ToolCatalogTests
{
    /// <summary>
    /// The <c>orkeon run</c> column, counted by hand from the matrix: Web core 5, search 5
    /// (web_search, brave_search, cache_search, semantic_search, local_embed_text), files 6,
    /// data 22, shell_command, session 6, EventHub 7, Analysis 15, collaboration 3 (human_input
    /// and the two per-agent coworker tools), list_mounts.
    /// </summary>
    private const int ToolsARunExposes = 71;

    // The package cell may carry a note after the code span (« `Orkeon.Hosting` (opt-in) »).
    [GeneratedRegex(@"^\| `([a-z0-9_]+)` \| `[A-Za-z]+` \| `Orkeon\.[A-Za-z.]+`[^|]*\|", RegexOptions.Multiline)]
    private static partial Regex InventoryRowPattern();

    private static string RepositoryRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));

    private static HashSet<string> DocumentedNames()
    {
        var inventory = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "tools", "inventory.md"));
        var table = inventory[inventory.IndexOf("### Names to use in YAML", StringComparison.Ordinal)..];
        return [.. InventoryRowPattern().Matches(table).Select(m => m.Groups[1].Value)];
    }

    [Fact]
    public void Every_catalogue_name_is_a_documented_registry_name()
    {
        var documented = DocumentedNames();
        Assert.NotEmpty(documented);

        var unknown = ToolCatalog.All.Select(t => t.Name).Where(n => !documented.Contains(n)).ToList();

        Assert.True(unknown.Count == 0, "Not in docs/tools/inventory.md: " + string.Join(", ", unknown));
    }

    [Fact]
    public void The_catalogue_lists_exactly_the_tools_a_run_exposes_once_each()
    {
        var names = ToolCatalog.All.Select(t => t.Name).ToList();

        Assert.Equal(ToolsARunExposes, names.Count);
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        // The three families the matrix keeps out of a run never appear: RAG, Slack, the
        // code interpreter (concrete type only), spawn_agent (wired by no shipped root).
        Assert.DoesNotContain("rag_search", names);
        Assert.DoesNotContain("slack_send_message", names);
        Assert.DoesNotContain("code_interpreter", names);
        Assert.DoesNotContain("spawn_agent", names);
    }

    [Fact]
    public void Every_key_requirement_points_at_a_key_the_card_offers()
    {
        var offered = ToolCatalog.Secrets.Select(s => s.EnvName).ToHashSet(StringComparer.Ordinal);
        var keyed = ToolCatalog.All
            .Where(t => t.Requirement is ToolRequirement.StoredKey or ToolRequirement.OnlyWithStoredKey)
            .ToList();

        Assert.Equal(2, keyed.Count);
        Assert.All(keyed, t => Assert.Contains(t.Argument!, offered));
        Assert.Equal(offered.Count, ToolCatalog.Secrets.Count);
        Assert.All(ToolCatalog.Secrets, s => Assert.Contains(ToolCatalog.All, t => t.Name == s.UsedBy));
    }

    [Fact]
    public void The_expert_setting_names_the_shell_section_the_runtime_reads()
    {
        var shell = Assert.Single(ToolCatalog.All, t => t.Requirement == ToolRequirement.ExpertSetting);

        Assert.Equal("shell_command", shell.Name);
        Assert.Equal(ShellToolsSection.SectionPath, shell.Argument);
        Assert.Equal("Orkeon:Tools:Shell", ShellToolsSection.SectionPath);
    }
}
