using Orkeon.Studio.Core.Events;
using Orkeon.Studio.Core.Forge;

namespace Orkeon.Studio.Core.Tests.Forge;

/// <summary>
/// The per-agent read of <c>blueprint.ready</c> (remediation v2, F-01): the Composer cards
/// need each agent's role, goal, backstory and own tools — not a deduplicated flat list —
/// and the agent editor needs the blueprint JSON verbatim to amend and send back.
/// </summary>
public sealed class ForgeBlueprintReadTests
{
    private static ForgeSessionModel Feed(string json)
    {
        var model = new ForgeSessionModel();
        Assert.True(OrkeonEventParser.TryParse(json, out var parsed), json);
        model.Feed(parsed!);
        return model;
    }

    private const string Blueprint =
        """{"v":2,"seq":1,"ts":"t","kind":"blueprint.ready","blueprint":{"crew":{"name":"veille","goal":"Résumer"},"agents":[{"key":"collecteur","role":"Web Researcher","goal":"Trouver les offres","backstory":"Ancien veilleur","tools":["web_scrape","file_read"]},{"key":"redacteur","role":"Writer","tools":["file_write","file_read"]}],"tasks":[{"key":"t1","description":"Collecter","agent":"collecteur"}],"rationale":"Deux rôles."},"iteration":1}""";

    [Fact]
    public void Each_agent_keeps_its_own_identity_and_tools()
    {
        var model = Feed(Blueprint);

        Assert.NotNull(model.Proposal);
        var agents = model.Proposal!.Agents;
        Assert.Equal(2, agents.Count);

        Assert.Equal("collecteur", agents[0].Key);
        Assert.Equal("Web Researcher", agents[0].Role);
        Assert.Equal("Trouver les offres", agents[0].Goal);
        Assert.Equal("Ancien veilleur", agents[0].Backstory);
        Assert.Equal(["web_scrape", "file_read"], agents[0].Tools);

        // Absent goal/backstory stay null; the shared file_read appears on both agents.
        Assert.Null(agents[1].Goal);
        Assert.Null(agents[1].Backstory);
        Assert.Equal(["file_write", "file_read"], agents[1].Tools);
    }

    [Fact]
    public void The_flat_tools_list_still_deduplicates_for_the_proposal_chips()
    {
        var model = Feed(Blueprint);

        Assert.Equal(["web_scrape", "file_read", "file_write"], model.Proposal!.Tools);
    }

    [Fact]
    public void The_blueprint_json_is_kept_verbatim_for_the_edit_round_trip()
    {
        var model = Feed(Blueprint);

        Assert.NotNull(model.BlueprintJson);
        // The stored node is the blueprint object itself — parseable, crew name intact.
        using var document = System.Text.Json.JsonDocument.Parse(model.BlueprintJson!);
        Assert.Equal("veille", document.RootElement.GetProperty("crew").GetProperty("name").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("agents").GetArrayLength());
    }
}
