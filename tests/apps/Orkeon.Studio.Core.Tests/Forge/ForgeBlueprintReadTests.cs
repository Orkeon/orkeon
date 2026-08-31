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
    public void Derived_mounts_come_from_reading_tools_and_deliverable_roots()
    {
        // file_read on board → /workspace read chip; two deliverables under /output →
        // ONE /output write chip; a rootless deliverable derives nothing.
        var model = Feed(
            """{"v":2,"seq":1,"ts":"t","kind":"blueprint.ready","blueprint":{"agents":[{"key":"a","role":"R","tools":["file_read","file_write"]}],"tasks":[{"key":"t1","agent":"a","deliverable":"/output/rapport.md"},{"key":"t2","agent":"a","deliverable":"/output/annexe.md"},{"key":"t3","agent":"a","deliverable":"sans-racine.md"}]},"iteration":1}""");

        // Compared field by field: the record now carries the roles behind each mount, and
        // a record's equality over a list is by reference.
        Assert.Equal(["/workspace", "/output"], model.DerivedMounts.Select(m => m.VirtualPath));
        Assert.Equal([false, true], model.DerivedMounts.Select(m => m.IsReadWrite));
    }

    /// <summary>
    /// Which agent implied which mount. The blueprint says it — per agent for the tools, per
    /// task for the deliverable — and the derivation used to flatten every agent's tools into
    /// one union, so the screen could only ever answer «somebody reads».
    /// <para>
    /// Provenance, never permission: the runtime mounts one flat list per host.
    /// </para>
    /// </summary>
    [Fact]
    public void Each_derived_mount_names_the_agents_behind_it()
    {
        var model = Feed(
            """{"v":2,"seq":1,"ts":"t","kind":"blueprint.ready","blueprint":{"agents":[{"key":"lecteur","role":"Lecteur","tools":["file_read"]},{"key":"redacteur","role":"Rédacteur","tools":["file_write"]}],"tasks":[{"key":"t1","agent":"redacteur","deliverable":"/output/rapport.md"}]},"iteration":1}""");

        var workspace = Assert.Single(model.DerivedMounts, m => m.VirtualPath == "/workspace");
        Assert.Equal(["Lecteur"], workspace.Agents);          // only the one holding a read tool

        var output = Assert.Single(model.DerivedMounts, m => m.VirtualPath == "/output");
        Assert.Equal(["Rédacteur"], output.Agents);           // the task's own agent
    }

    [Fact]
    public void One_mount_written_by_two_agents_names_both_once()
    {
        var model = Feed(
            """{"v":2,"seq":1,"ts":"t","kind":"blueprint.ready","blueprint":{"agents":[{"key":"a","role":"A","tools":["file_write"]},{"key":"b","role":"B","tools":["file_write"]}],"tasks":[{"key":"t1","agent":"a","deliverable":"/output/x.md"},{"key":"t2","agent":"b","deliverable":"/output/y.md"},{"key":"t3","agent":"a","deliverable":"/output/z.md"}]},"iteration":1}""");

        var output = Assert.Single(model.DerivedMounts, m => m.VirtualPath == "/output");
        Assert.Equal(["A", "B"], output.Agents);
    }

    [Fact]
    public void An_edited_blueprint_recomputes_the_derived_mounts()
    {
        var model = Feed(Blueprint);
        var initial = Assert.Single(model.DerivedMounts);
        Assert.Equal("/workspace", initial.VirtualPath);
        Assert.False(initial.IsReadWrite);

        // The re-emitted blueprint (after an edit) drops every reading tool and gains a
        // deliverable: the chips follow the agents, they are never sticky.
        Assert.True(OrkeonEventParser.TryParse(
            """{"v":2,"seq":2,"ts":"t","kind":"blueprint.ready","blueprint":{"agents":[{"key":"a","role":"R","tools":["file_write"]}],"tasks":[{"key":"t1","agent":"a","deliverable":"/sortie/doc.md"}]},"iteration":2}""",
            out var edited));
        model.Feed(edited!);

        var after = Assert.Single(model.DerivedMounts);
        Assert.Equal("/sortie", after.VirtualPath);
        Assert.True(after.IsReadWrite);
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
