using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Orkeon.Domain.Attributes;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Tools;
using Orkeon.Tools.Email.DependencyInjection;

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
    /// (web_search, brave_search, cache_search, semantic_search, local_embed_text), files 5,
    /// data 22, e-mail 13 (the twelve account tools and email_parser, which left the files
    /// family), shell_command, session 6, EventHub 7, Analysis 15, collaboration 3 (human_input
    /// and the two per-agent coworker tools), list_mounts.
    /// </summary>
    private const int ToolsARunExposes = 83;

    /// <summary>The one e-mail tool that works on a file rather than on an account.</summary>
    private const string EmailParser = "email_parser";

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

        Assert.Equal(3, keyed.Count);
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

    /// <summary>
    /// MAIL-05 — the e-mail family is pinned against the code, not only against the inventory:
    /// it carries exactly the tool contracts the e-mail assembly declares, so a tool added
    /// there and forgotten here, or a name misspelt here, fails on its own.
    /// </summary>
    [Fact]
    public void The_email_family_carries_every_tool_the_email_assembly_declares()
    {
        var email = Assert.Single(ToolCatalog.Families, f => f.Key == ToolCatalog.EmailFamily);
        var declared = typeof(EmailToolsServiceCollectionExtensions).Assembly.GetTypes()
            .Select(type => type.GetCustomAttribute<ToolContractAttribute>()?.UniqueName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(13, declared.Count);
        Assert.Equal(declared, email.Tools.Select(t => t.Name).Order(StringComparer.Ordinal));
        // email_parser changed family: it is listed once, here, and no longer under Files.
        var files = Assert.Single(ToolCatalog.Families, f => f.Key == ToolCatalog.FilesFamily);
        Assert.DoesNotContain(files.Tools, t => t.Name == EmailParser);
    }

    /// <summary>
    /// Without an account the twelve mailbox tools are still registered and refuse every call,
    /// so what they need is neither a key nor a call parameter: an account declared in the
    /// section the runtime binds. The parser reads an .eml file and needs nothing.
    /// </summary>
    [Fact]
    public void The_email_account_requirement_names_the_email_section_the_runtime_binds()
    {
        var needAccount = ToolCatalog.All.Where(t => t.Requirement == ToolRequirement.EmailAccount).ToList();
        var email = Assert.Single(ToolCatalog.Families, f => f.Key == ToolCatalog.EmailFamily);

        Assert.Equal(12, needAccount.Count);
        Assert.All(needAccount, t =>
        {
            Assert.Contains(t, email.Tools);
            Assert.Equal(ToolCatalog.EmailAccounts, t.Argument);
        });
        Assert.Equal("Orkeon:Tools:Email:Accounts", ToolCatalog.EmailAccounts);
        Assert.Equal(ToolRequirement.None, Assert.Single(email.Tools, t => t.Name == EmailParser).Requirement);
    }

    /// <summary>
    /// STUDIO-67 — Studio has a form for the accounts, so the sentence of the requirement sends to
    /// Settings › E-mail: it names neither the section of the file nor <c>orkeon email login</c>.
    /// </summary>
    [Fact]
    public void The_email_account_requirement_reads_as_a_pointer_to_the_email_settings_tab()
    {
        var sentence = EnglishStudioStrings.Instance[StudioStringKeys.ToolNeedsEmailAccount];

        Assert.Equal("needs an e-mail account, declared in Settings › E-mail", sentence);
        Assert.Contains(EnglishStudioStrings.Instance[StudioStringKeys.ShellMails], sentence, StringComparison.Ordinal);
        Assert.DoesNotContain("{0}", sentence, StringComparison.Ordinal);
    }
}
