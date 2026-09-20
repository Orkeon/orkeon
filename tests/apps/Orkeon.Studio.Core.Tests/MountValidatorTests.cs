using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Tests.Doubles;
using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// Mount validation mirrors what the runtime checks at boot: the list is non-empty,
/// every entry parses, every physical path exists, and virtual paths are unique.
/// </summary>
public sealed class MountValidatorTests
{
    private static readonly string[] CreatedDirectories = ["/srv/data"];

    private static MountDefinition Mount(string physical, string virtualPath, MountRights rights = MountRights.ReadOnly) =>
        new() { PhysicalPath = physical, VirtualPath = virtualPath, Rights = rights };

    [Fact]
    public void A_well_formed_list_over_existing_folders_produces_no_message()
    {
        var validator = new MountValidator(new FakeDirectoryProbe("/srv/data", "/srv/out"));

        var messages = validator.Validate(
            [Mount("/srv/data", "/workspace"), Mount("/srv/out", "/output", MountRights.ReadWrite)]);

        Assert.Empty(messages);
    }

    [Fact]
    public void A_missing_physical_path_is_an_error()
    {
        var validator = new MountValidator(new FakeDirectoryProbe("/srv/data"));

        var messages = validator.Validate([Mount("/srv/data", "/workspace"), Mount("/nope", "/output")]);

        var message = Assert.Single(messages);
        Assert.Equal(ValidationCodes.MountPathMissing, message.Code);
        Assert.Equal(ValidationSeverity.Error, message.Severity);
        Assert.Contains("/nope", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_list_is_an_error_when_at_least_one_mount_is_required()
    {
        var messages = new MountValidator(new FakeDirectoryProbe()).Validate(Array.Empty<MountDefinition>());

        Assert.Equal(ValidationCodes.MountsEmpty, Assert.Single(messages).Code);
    }

    [Fact]
    public void An_empty_list_is_accepted_when_mounts_come_from_elsewhere()
    {
        var messages = new MountValidator(new FakeDirectoryProbe())
            .Validate(Array.Empty<MountDefinition>(), requireAtLeastOne: false);

        Assert.Empty(messages);
    }

    [Fact]
    public void Two_mounts_on_the_same_virtual_path_collide()
    {
        var validator = new MountValidator(new FakeDirectoryProbe("/a", "/b"));

        var messages = validator.Validate([Mount("/a", "/workspace"), Mount("/b", "/workspace/")]);

        var message = Assert.Single(messages);
        Assert.Equal(ValidationCodes.MountVirtualCollision, message.Code);
        Assert.Contains("/workspace", message.Text, StringComparison.Ordinal);
    }

    /// <summary>VFS-90 D-03: two entries may share a root when both carry an id — information, never a refusal.</summary>
    [Fact]
    public void Two_entries_of_one_root_that_both_carry_an_id_are_information_not_an_error()
    {
        var validator = new MountValidator(new FakeDirectoryProbe("/a", "/b"));

        var messages = validator.Validate(
            [Mount("/a", "/output", MountRights.ReadWrite).WithFreshId(), Mount("/b", "/output/", MountRights.ReadWrite).WithFreshId()]);

        var message = Assert.Single(messages);
        Assert.Equal(ValidationCodes.MountSharedRoot, message.Code);
        Assert.Equal(ValidationSeverity.Information, message.Severity);
        Assert.Contains("declared 2 times", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shared_root_with_an_entry_that_has_no_id_is_an_error_naming_that_entry()
    {
        var validator = new MountValidator(new FakeDirectoryProbe("/a", "/b"));

        var messages = validator.Validate([Mount("/a", "/output").WithFreshId(), Mount("/b", "/output")]);

        var message = Assert.Single(messages);
        Assert.Equal(ValidationCodes.MountVirtualCollision, message.Code);
        Assert.Equal(ValidationSeverity.Error, message.Severity);
        Assert.Contains("'/b:/output:ro' has no id", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void One_id_on_two_entries_is_an_error()
    {
        var validator = new MountValidator(new FakeDirectoryProbe("/a", "/b"));
        var id = Orkeon.Domain.Common.MountId.Create();

        var messages = validator.Validate(
            [Mount("/a", "/workspace") with { Id = id }, Mount("/b", "/output") with { Id = id }]);

        var message = Assert.Single(messages);
        Assert.Equal(ValidationCodes.MountIdDuplicate, message.Code);
        Assert.Equal(ValidationSeverity.Error, message.Severity);
        Assert.Equal(id.ToString(), message.Path);
    }

    [Fact]
    public void One_folder_declared_twice_under_one_root_is_a_warning_beside_the_information()
    {
        var validator = new MountValidator(new FakeDirectoryProbe("/a"));

        var messages = validator.Validate(
            [Mount("/a", "/output").WithFreshId(), Mount("/a", "/output", MountRights.ReadWrite).WithFreshId()]);

        Assert.Equal(2, messages.Count);
        Assert.Contains(messages, m => m.Severity == ValidationSeverity.Information && m.Code == ValidationCodes.MountSharedRoot);
        Assert.Contains(messages, m => m.Severity == ValidationSeverity.Warning && m.Text.Contains("one entry is enough", StringComparison.Ordinal));
    }

    [Fact]
    public void Distinct_virtual_paths_do_not_collide()
    {
        var validator = new MountValidator(new FakeDirectoryProbe("/a", "/b"));

        var messages = validator.Validate([Mount("/a", "/workspace"), Mount("/b", "/workspace-2")]);

        Assert.Empty(messages);
    }

    [Fact]
    public void An_unparsable_raw_entry_is_reported_with_its_index()
    {
        var validator = new MountValidator(new FakeDirectoryProbe("/srv/data"));

        var messages = validator.Validate(["/srv/data:/workspace:ro", "garbage"]);

        var message = Assert.Single(messages);
        Assert.Equal(ValidationCodes.MountFormat, message.Code);
        Assert.Contains("index 1", message.Text, StringComparison.Ordinal);
        Assert.Equal("garbage", message.Path);
    }

    [Fact]
    public void An_invalid_virtual_path_typed_in_the_form_is_reported_before_the_parser_sees_it()
    {
        var validator = new MountValidator(new FakeDirectoryProbe("/srv/data"));

        var messages = validator.Validate([Mount("/srv/data", "workspace")]);

        Assert.Equal(ValidationCodes.MountFormat, Assert.Single(messages).Code);
    }

    [Fact]
    public void Raw_entries_are_validated_against_the_disk_too()
    {
        var validator = new MountValidator(new FakeDirectoryProbe());

        var messages = validator.Validate(["/srv/data:/workspace:ro"]);

        Assert.Equal(ValidationCodes.MountPathMissing, Assert.Single(messages).Code);
    }

    [Fact]
    public void Creating_the_missing_folder_clears_the_error()
    {
        var probe = new FakeDirectoryProbe();
        var validator = new MountValidator(probe);

        Assert.NotEmpty(validator.Validate(["/srv/data:/workspace:ro"]));

        probe.Create("/srv/data");

        Assert.Empty(validator.Validate(["/srv/data:/workspace:ro"]));
        Assert.Equal(CreatedDirectories, probe.Created);
    }

    /// <summary>
    /// A team-relative entry (<c>./output:/output:rw</c>, STUDIO-14) names a folder that is
    /// born at adoption. Before the team exists there is nothing to probe, and probing the
    /// spelling against Studio's own working directory would report a mistake nobody made.
    /// Every other entry is checked exactly as before.
    /// </summary>
    [Fact]
    public void Should_SkipExistence_When_TheEntryIsTeamRelativeAndNoTeamFolderIsKnown()
    {
        var validator = new MountValidator(new FakeDirectoryProbe("/srv/data"));

        var messages = validator.Validate(
            ["./output:/output:rw", "./input:/workspace:ro", "/srv/data:/docs:ro", "/nope:/x:ro"],
            requireAtLeastOne: false);

        var message = Assert.Single(messages);
        Assert.Equal(ValidationCodes.MountPathMissing, message.Code);
        Assert.Contains("/nope", message.Text, StringComparison.Ordinal);
        Assert.Empty(validator.Validate([Mount("./output", "/output", MountRights.ReadWrite)]));
    }

    [Fact]
    public void Should_CheckExistenceUnderTheTeamFolder_When_OneIsGiven()
    {
        var team = Path.Combine("/teams", "veille");
        var validator = new MountValidator(new FakeDirectoryProbe(Path.Combine(team, "output")));

        Assert.Empty(validator.Validate(["./output:/output:rw"], requireAtLeastOne: false, teamDirectory: team));

        var messages = validator.Validate(["./input:/workspace:ro"], requireAtLeastOne: false, teamDirectory: team);

        var message = Assert.Single(messages);
        Assert.Equal(ValidationCodes.MountPathMissing, message.Code);
        Assert.Contains("./input", message.Text, StringComparison.Ordinal);
        // The structured overload takes the same folder.
        Assert.Empty(validator.Validate([Mount("./output", "/output", MountRights.ReadWrite)], teamDirectory: team));
        Assert.Single(validator.Validate([Mount("./input", "/workspace")], teamDirectory: team));
    }
}
