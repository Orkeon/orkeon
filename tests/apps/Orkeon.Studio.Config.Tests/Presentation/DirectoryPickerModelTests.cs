using Orkeon.Studio.Config.Presentation;
using Orkeon.Studio.Config.Tests.Doubles;

namespace Orkeon.Studio.Config.Tests.Presentation;

public class DirectoryPickerModelTests
{
    private static FakeDirectoryLister Tree() => new FakeDirectoryLister()
        .WithDirectory(Path.Combine("/", "data", "workspace"))
        .WithDirectory(Path.Combine("/", "data", "output"));

    [Fact]
    public void The_picker_starts_where_it_was_told_to()
    {
        var model = new DirectoryPickerModel(Path.Combine("/", "data"), Tree());

        Assert.Equal(Path.GetFullPath(Path.Combine("/", "data")), model.CurrentPath);
    }

    [Fact]
    public void Entries_list_the_parent_first_then_the_sub_directories()
    {
        var model = new DirectoryPickerModel(Path.Combine("/", "data"), Tree());

        var entries = model.Entries;

        Assert.Equal(DirectoryPickerModel.ParentEntry, entries[0]);
        Assert.Contains("workspace", entries);
        Assert.Contains("output", entries);
    }

    [Fact]
    public void Entering_a_sub_directory_moves_into_it()
    {
        var model = new DirectoryPickerModel(Path.Combine("/", "data"), Tree());

        model.Enter("workspace");

        Assert.Equal(Path.Combine("/", "data", "workspace"), model.CurrentPath);
    }

    [Fact]
    public void Entering_the_parent_entry_walks_up()
    {
        var model = new DirectoryPickerModel(Path.Combine("/", "data", "workspace"), Tree());

        model.Enter(DirectoryPickerModel.ParentEntry);

        Assert.Equal(Path.Combine("/", "data"), model.CurrentPath);
    }

    [Fact]
    public void An_entry_that_does_not_exist_leaves_the_picker_where_it_was()
    {
        var model = new DirectoryPickerModel(Path.Combine("/", "data"), Tree());

        model.Enter("nope");

        Assert.Equal(Path.GetFullPath(Path.Combine("/", "data")), model.CurrentPath);
    }

    [Fact]
    public void An_index_outside_the_list_is_ignored()
    {
        var model = new DirectoryPickerModel(Path.Combine("/", "data"), Tree());

        model.Enter(42);

        Assert.Equal(Path.GetFullPath(Path.Combine("/", "data")), model.CurrentPath);
    }

    [Fact]
    public void Navigating_to_an_unknown_absolute_path_fails_without_moving()
    {
        var model = new DirectoryPickerModel(Path.Combine("/", "data"), Tree());

        Assert.False(model.TryNavigateTo(Path.Combine("/", "elsewhere")));
        Assert.Equal(Path.GetFullPath(Path.Combine("/", "data")), model.CurrentPath);

        Assert.True(model.TryNavigateTo(Path.Combine("/", "data", "output")));
        Assert.Equal(Path.Combine("/", "data", "output"), model.CurrentPath);
    }
}
