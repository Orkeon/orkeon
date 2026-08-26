using Orkeon.Studio.Core.Events;
using Orkeon.Studio.Core.Forge;

namespace Orkeon.Studio.Core.Tests.Forge;

/// <summary>
/// The blueprint→mount derivation Studio shows as chips and writes into an adopted team's
/// sidecar. It is the second implementation of <c>ForgePromoter.DeliverableMounts</c> — the
/// CLI's — and Studio.Core deliberately does not reference the CLI, so this suite is the pin.
/// <para>
/// The guards below were added to the CLI copy alone. A deliverable naming a reserved root or
/// a traversal segment therefore reached the sidecar through Studio and nowhere else, and the
/// team it produced was one Studio could launch and the runner refused at start — the failure
/// the guards exist to prevent, arriving through the one door that had none.
/// </para>
/// </summary>
public sealed class ForgeDerivedMountTests
{
    private static IReadOnlyList<ForgeDerivedMount> Derive(string toolsJson, string deliverablesJson)
    {
        var json = """{"v":2,"seq":1,"ts":"t","kind":"blueprint.ready","iteration":1,"blueprint":{"crew":{"name":"t","goal":"g"},"agents":[{"key":"a","role":"A","goal":"G","tools":TOOLS}],"tasks":TASKS}}"""
            .Replace("TOOLS", toolsJson, StringComparison.Ordinal)
            .Replace("TASKS", deliverablesJson, StringComparison.Ordinal);

        var model = new ForgeSessionModel();
        Assert.True(OrkeonEventParser.TryParse(json, out var parsed), json);
        model.Feed(parsed!);
        return model.DerivedMounts;
    }

    private static string Tasks(params string[] deliverables) =>
        "[" + string.Join(",", deliverables.Select((d, i) =>
            $$"""{"key":"t{{i}}","description":"d","expectedOutput":"e","agent":"a","deliverable":"{{d}}"}""")) + "]";

    [Fact]
    public void A_deliverable_root_becomes_a_write_mount()
    {
        var mounts = Derive("[]", Tasks("/output/rapport.md", "/archive/2026.csv"));

        Assert.Equal(["/output", "/archive"], mounts.Where(m => m.IsReadWrite).Select(m => m.VirtualPath));
    }

    [Fact]
    public void A_reading_tool_implies_the_workspace_read_mount()
    {
        var mounts = Derive("""["file_read"]""", Tasks("/output/r.md"));

        Assert.Contains(mounts, m => m.VirtualPath == "/workspace" && !m.IsReadWrite);
    }

    /// <summary>
    /// The runner reserves these for itself; a team carrying one is refused at start
    /// (ADR-008, decision 5). The CLI's copy skipped them; this one did not.
    /// </summary>
    [Theory]
    [InlineData("/crew")]
    [InlineData("/script")]
    [InlineData("/llm-logs")]
    public void A_reserved_virtual_root_is_never_derived(string reserved)
    {
        var mounts = Derive("[]", Tasks($"{reserved}/notes.md", "/output/r.md"));

        Assert.DoesNotContain(mounts, m => m.VirtualPath == reserved);
        Assert.Contains(mounts, m => m.VirtualPath == "/output");
    }

    /// <summary>
    /// The blueprint is LLM-authored: a traversal segment would put the team's own files
    /// outside the folder it was adopted into.
    /// </summary>
    [Theory]
    [InlineData("/../escaped/r.md")]
    [InlineData("/./r.md")]
    public void A_traversal_root_is_never_derived(string deliverable)
    {
        var mounts = Derive("[]", Tasks(deliverable, "/output/r.md"));

        Assert.Equal(["/output"], mounts.Where(m => m.IsReadWrite).Select(m => m.VirtualPath));
    }

    [Fact]
    public void The_same_root_is_derived_once()
    {
        var mounts = Derive("[]", Tasks("/output/a.md", "/output/b.md"));

        Assert.Single(mounts.Where(m => m.IsReadWrite));
    }

    [Fact]
    public void A_deliverable_that_is_not_an_absolute_virtual_path_is_ignored()
    {
        var mounts = Derive("[]", Tasks("rapport.md", "/"));

        Assert.Empty(mounts);
    }
}
