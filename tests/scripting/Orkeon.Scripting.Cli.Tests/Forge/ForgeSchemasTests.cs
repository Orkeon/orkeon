using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>Shared valid documents for the schema, compiler and stage tests.</summary>
internal static class ForgeDocuments
{
    /// <summary>A schema-valid brief, French problem included.</summary>
    public const string ValidBrief = """
        {
          "goal": "Résumer chaque matin les nouvelles offres du fournisseur",
          "context": "Achat de composants électroniques",
          "inputs": [ { "name": "supplier_url", "description": "La page des offres", "example": "https://exemple.fr/offres" } ],
          "expectedOutput": { "format": "markdown", "description": "Un résumé d'une page, sources citées" },
          "constraints": [ "moins d'une page", "en français" ],
          "toolHints": [ "lire une page web" ],
          "acceptance": [
            { "id": "A1", "statement": "Le résumé cite ses sources", "kind": "must" },
            { "id": "A2", "statement": "Moins d'une page", "kind": "should" }
          ],
          "sample": { "variables": { "supplier_url": "https://exemple.fr/offres" }, "initialContext": "Premier essai" },
          "language": "fr"
        }
        """;

    /// <summary>A schema-valid blueprint using only the tools the tests declare as known.</summary>
    public const string ValidBlueprint = """
        {
          "crew": { "name": "veille-fournisseur", "goal": "Résumer les nouvelles offres", "process": "sequential" },
          "agents": [
            { "key": "collecteur", "role": "Web Researcher", "goal": "Collecter les offres", "tools": [ "web_scrape" ] },
            { "key": "redacteur", "role": "Writer", "goal": "Rédiger le résumé", "tools": [ "file_write" ] }
          ],
          "tasks": [
            { "key": "collecte", "description": "Collecter les offres du jour", "expectedOutput": "La liste brute", "agent": "collecteur" },
            { "key": "resume", "description": "Rédiger le résumé", "expectedOutput": "Le résumé cité",
              "agent": "redacteur", "dependencies": [ "collecte" ], "deliverable": "/output/resume.md" }
          ],
          "rationale": "Deux rôles séparés: collecter puis rédiger, séquentiel."
        }
        """;

    /// <summary>The catalogue the valid blueprint draws from.</summary>
    public static readonly IReadOnlyCollection<string> KnownTools = ["web_scrape", "file_write", "file_read"];
}

/// <summary>
/// The brief schema (SPEC-ORKEON-FORGE §7.2): submissions are validated, never trusted on
/// format alone, and every failure is a sentence the repair prompt can carry.
/// </summary>
public class ForgeBriefTests
{
    [Fact]
    public void A_valid_brief_parses_with_its_criteria()
    {
        Assert.True(ForgeBrief.TryParse(ForgeDocuments.ValidBrief, out var brief, out var errors));
        Assert.Empty(errors);
        Assert.Equal("Résumer chaque matin les nouvelles offres du fournisseur", brief!.Goal);
        Assert.Equal(2, brief.Acceptance!.Count);
        Assert.Equal("must", brief.Acceptance[0].Kind);
        Assert.Equal("https://exemple.fr/offres", brief.Sample!.Variables!["supplier_url"]);
    }

    [Fact]
    public void A_brief_without_goal_or_acceptance_is_refused_with_both_errors()
    {
        Assert.False(ForgeBrief.TryParse("""{ "context": "..." }""", out _, out var errors));
        Assert.Contains(errors, e => e.Contains("'goal'", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("'acceptance'", StringComparison.Ordinal));
    }

    [Fact]
    public void An_acceptance_criterion_needs_id_statement_and_a_known_kind()
    {
        var json = """{ "goal": "g", "acceptance": [ { "kind": "shall" } ] }""";

        Assert.False(ForgeBrief.TryParse(json, out _, out var errors));
        Assert.Contains(errors, e => e.Contains("'id'", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("'statement'", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("'shall'", StringComparison.Ordinal));
    }

    [Fact]
    public void Malformed_json_is_an_error_message_not_an_exception()
    {
        Assert.False(ForgeBrief.TryParse("{ not json", out _, out var errors));
        Assert.Contains(errors, e => e.Contains("not valid JSON", StringComparison.Ordinal));
    }
}

/// <summary>
/// The blueprint schema (SPEC-ORKEON-FORGE §7.3): structural rules here, referential rules
/// in the shared validator — nothing verified twice in two spellings.
/// </summary>
public class ForgeBlueprintTests
{
    [Fact]
    public void A_valid_blueprint_parses_with_its_entities()
    {
        Assert.True(ForgeBlueprint.TryParse(ForgeDocuments.ValidBlueprint, out var blueprint, out var errors));
        Assert.Empty(errors);
        Assert.Equal(2, blueprint!.Agents!.Count);
        Assert.Equal(["collecte"], blueprint.Tasks![1].Dependencies);
        Assert.Equal("/output/resume.md", blueprint.Tasks[1].Deliverable);
    }

    [Fact]
    public void Empty_sections_and_missing_fields_are_each_named()
    {
        var json = """{ "crew": { "name": "x" }, "agents": [], "tasks": [ { "key": "t" } ] }""";

        Assert.False(ForgeBlueprint.TryParse(json, out _, out var errors));
        Assert.Contains(errors, e => e.Contains("'crew.goal'", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("'agents' must hold", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("'description'", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("'agent'", StringComparison.Ordinal));
    }

    [Fact]
    public void A_goal_length_crew_name_is_refused_as_a_display_name()
    {
        // v3 W-07: crew.name becomes the team's display name and folder slug — a
        // sentence-long name gets the plan rejected with a repairable error.
        var longName = new string('x', 61);
        var json = $$"""
            {
              "crew": { "name": "{{longName}}", "goal": "g" },
              "agents": [ { "key": "a", "role": "r", "goal": "g" } ],
              "tasks": [ { "key": "t", "description": "d", "expectedOutput": "o", "agent": "a" } ]
            }
            """;

        Assert.False(ForgeBlueprint.TryParse(json, out _, out var errors));
        Assert.Contains(errors, e => e.Contains("short display name", StringComparison.Ordinal));
    }

    [Fact]
    public void Duplicate_keys_are_refused()
    {
        var json = """
            {
              "crew": { "name": "x", "goal": "g" },
              "agents": [
                { "key": "a", "role": "r", "goal": "g" },
                { "key": "a", "role": "r2", "goal": "g2" }
              ],
              "tasks": [ { "key": "t", "description": "d", "expectedOutput": "o", "agent": "a" } ]
            }
            """;

        Assert.False(ForgeBlueprint.TryParse(json, out _, out var errors));
        Assert.Contains(errors, e => e.Contains("used twice", StringComparison.Ordinal));
    }
}
