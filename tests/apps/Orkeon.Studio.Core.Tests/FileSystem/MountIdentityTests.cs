using Orkeon.Domain.Common;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.FileSystem;

namespace Orkeon.Studio.Core.Tests.FileSystem;

/// <summary>
/// A settings entry has an identity (VFS-90): the helpers the editors and the teams reason
/// with, and the section's rule that every save gives an id to an entry that has none.
/// </summary>
public sealed class MountIdentityTests
{
    private static MountDefinition Mount(string physical, string root, MountRights rights = MountRights.ReadOnly, MountId? id = null) =>
        new() { Id = id, PhysicalPath = physical, VirtualPath = root, Rights = rights };

    [Fact]
    public void A_fresh_id_is_a_new_one_each_time_and_can_be_dropped_again()
    {
        var mount = Mount("/srv/out", "/output");

        var first = mount.WithFreshId();
        var second = mount.WithFreshId();

        Assert.NotNull(first.Id);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(6, first.ShortId!.Length);
        Assert.EndsWith(first.ShortId, first.Id!.ToString(), StringComparison.Ordinal);
        Assert.Null(first.WithoutId().Id);
        Assert.Null(mount.ShortId);
    }

    [Fact]
    public void The_same_declaration_ignores_ids_and_spelling_noise()
    {
        var a = Mount("/srv/out/", "/output/", MountRights.ReadWrite, MountId.Create());
        var b = Mount("/srv/out", "/output", MountRights.ReadWrite, MountId.Create());

        Assert.True(a.SameDeclaration(b));
        Assert.False(a.SameIdentity(b));
        Assert.True(a.SameIdentity(a with { PhysicalPath = "/elsewhere" }));
        Assert.False(a.SameDeclaration(b with { Rights = MountRights.ReadOnly }));
        Assert.False(a.SameDeclaration(b with { VirtualPath = "/docs" }));
        Assert.True(a.SameRootAs("/output/"));
        Assert.Equal("/", MountDefinition.NormalizeRoot("/"));
        Assert.Equal("/output", MountDefinition.NormalizeRoot(" /output/ "));
    }

    [Fact]
    public void Saving_the_section_gives_every_entry_an_id_and_keeps_an_existing_one()
    {
        var document = AppSettingsDocument.CreateEmpty();
        var kept = MountId.Create();

        document.Mounts.Set([Mount("/srv/data", "/workspace"), Mount("/srv/out", "/output", MountRights.ReadWrite, kept)]);

        var definitions = document.Mounts.Definitions;
        Assert.Equal(2, definitions.Count);
        Assert.NotNull(definitions[0].Id);
        Assert.Equal(kept, definitions[1].Id);
        Assert.Equal(kept, document.Mounts.Find(kept)!.Id);
        Assert.Equal(1, document.Mounts.IndexOf(kept));
        Assert.Equal(-1, document.Mounts.IndexOf(MountId.Create()));
    }

    [Fact]
    public void Assigning_ids_leaves_unparsable_entries_alone_and_reports_whether_anything_changed()
    {
        var document = AppSettingsDocument.CreateEmpty();
        document.Mounts.SetRaw(["/srv/data:/workspace:ro", "garbage"]);

        Assert.True(document.Mounts.WithIdsAssigned());
        Assert.False(document.Mounts.WithIdsAssigned());

        var raw = document.Mounts.RawEntries;
        Assert.Equal("garbage", raw[1]);
        Assert.NotNull(MountDefinition.Parse(raw[0]).Id);
        Assert.Equal("/srv/data:/workspace:ro", MountDefinition.Parse(raw[0]).WithoutId().ToMountString());
    }

    [Fact]
    public void Ensuring_a_declaration_reuses_an_equal_entry_and_otherwise_adds_one_even_on_a_taken_root()
    {
        var document = AppSettingsDocument.CreateEmpty();
        document.Mounts.SetRaw(["/srv/out:/output:rw"]);

        // Same folder, root and rights: the existing entry, now with an id, not a second one.
        var reused = document.Mounts.EnsureDeclared(Mount("/srv/out/", "/output", MountRights.ReadWrite));
        Assert.NotNull(reused.Id);
        Assert.Single(document.Mounts.RawEntries);
        Assert.Equal(reused.ToMountString(), document.Mounts.RawEntries[0]);

        // Another folder under the same root: a second /output, told apart by its id (D-01).
        var added = document.Mounts.EnsureDeclared(Mount("/srv/other", "/output", MountRights.ReadWrite));
        Assert.NotNull(added.Id);
        Assert.NotEqual(reused.Id, added.Id);
        Assert.Equal(2, document.Mounts.RawEntries.Count);
        Assert.Equal(added.ToMountString(), document.Mounts.RawEntries[1]);

        // Asked again: nothing new.
        Assert.Equal(added.Id, document.Mounts.EnsureDeclared(Mount("/srv/other", "/output", MountRights.ReadWrite)).Id);
        Assert.Equal(2, document.Mounts.RawEntries.Count);
    }

    /// <summary>D-06: a copy from another machine keeps its id here, unless this file already spends it.</summary>
    [Fact]
    public void Ensuring_a_declaration_that_arrives_with_an_id_keeps_it_unless_the_id_is_taken()
    {
        var document = AppSettingsDocument.CreateEmpty();
        var travelling = MountId.Create();

        var kept = document.Mounts.EnsureDeclared(Mount("/home/me/out", "/output", MountRights.ReadWrite, travelling));
        Assert.Equal(travelling, kept.Id);

        var renamed = document.Mounts.EnsureDeclared(Mount("/home/me/docs", "/docs", MountRights.ReadOnly, travelling));
        Assert.NotEqual(travelling, renamed.Id);
        Assert.Equal(2, document.Mounts.RawEntries.Count);
    }
}
