using Orkeon.Domain.Common;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Domain.Tests.FileSystem;

/// <summary>
/// VFS-90: what a crew writes under <c>mounts:</c> — a root, or a root pinned to one settings
/// entry by id. The root is always there, so a reference survives a machine where the id is
/// unknown as long as something provides the root.
/// </summary>
public class MountReferenceTests
{
    private const string Id = "01J9Z3K4M5N6P7Q8R9S0T1V2W3";

    [Fact]
    public void A_root_alone_is_a_reference_without_id()
    {
        var reference = MountReference.Parse("/output");

        Assert.Null(reference.Id);
        Assert.Equal("/output", reference.VirtualRoot);
        Assert.Equal("/output", reference.ToString());
    }

    [Fact]
    public void An_id_and_a_root_make_a_pinned_reference()
    {
        var reference = MountReference.Parse($"{Id}|/output");

        Assert.Equal(Id, reference.Id!.ToString());
        Assert.Equal("/output", reference.VirtualRoot);
        Assert.Equal($"{Id}|/output", reference.ToString());
    }

    [Theory]
    [InlineData("/output/", "/output")]
    [InlineData("  /output  ", "/output")]
    [InlineData("/", "/")]
    public void The_root_is_normalized_without_its_trailing_slash(string text, string expected) =>
        Assert.Equal(expected, MountReference.Parse(text).VirtualRoot);

    [Theory]
    [InlineData("output")]                                  // not rooted
    [InlineData(@"C:\data")]                                 // a physical path is never a reference (ADR-008)
    [InlineData("01J9Z3K4M5N6P7Q8R9S0T1V2W3")]               // a bare id has no root to fall back on
    [InlineData("01J9Z3K4M5N6P7Q8R9S0T1V2W3|output")]        // pinned, but not rooted
    [InlineData("nope|/output")]                             // not an id
    [InlineData("")]
    [InlineData("   ")]
    public void Anything_else_is_refused_with_the_two_accepted_forms(string text)
    {
        Assert.False(MountReference.TryParse(text, out _));
        var failure = Assert.Throws<FormatException>(() => MountReference.Parse(text));
        Assert.Contains("'/root'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("'<ulid>|/root'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_references_on_one_root_say_so_whatever_their_ids()
    {
        var byRoot = MountReference.ForRoot("/output/");
        var byId = MountReference.ForId(MountId.Create(), "/output");

        Assert.True(byRoot.SameRootAs(byId));
        Assert.False(byRoot.SameRootAs(MountReference.ForRoot("/workspace")));
    }
}
