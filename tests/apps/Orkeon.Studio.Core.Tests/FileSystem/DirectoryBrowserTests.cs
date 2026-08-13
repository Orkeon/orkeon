using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Tests.Doubles;

namespace Orkeon.Studio.Core.Tests.FileSystem;

public class DirectoryBrowserTests
{
    private static FakeDirectoryLister Tree() => new FakeDirectoryLister()
        .WithDirectory(Path.Combine("/", "data", "workspace"))
        .WithDirectory(Path.Combine("/", "data", "output"))
        .WithFile(Path.Combine("/", "data", "appsettings.json"))
        .WithFile(Path.Combine("/", "data", "appsettings.Development.json"))
        .WithFile(Path.Combine("/", "data", "notes.txt"));

    private static DirectoryBrowser At(string path, string? pattern = null) =>
        new(path, Tree(), pattern);

    private static int IndexOf(IReadOnlyList<string> entries, string name)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            if (string.Equals(entries[i], name, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    [Fact]
    public void The_browser_starts_where_it_was_told_to()
    {
        var browser = At(Path.Combine("/", "data"));

        Assert.Equal(Path.GetFullPath(Path.Combine("/", "data")), browser.CurrentPath);
    }

    [Fact]
    public void Entries_list_the_parent_first_then_the_sub_directories()
    {
        var entries = At(Path.Combine("/", "data")).Entries;

        Assert.Equal(DirectoryBrowser.ParentEntry, entries[0]);
        Assert.Contains("workspace", entries);
        Assert.Contains("output", entries);
    }

    [Fact]
    public void Files_are_not_listed_without_a_pattern()
    {
        var browser = At(Path.Combine("/", "data"));

        Assert.False(browser.ListsFiles);
        Assert.DoesNotContain("appsettings.json", browser.Entries);
    }

    [Fact]
    public void A_pattern_lists_the_matching_files_after_the_directories()
    {
        var browser = At(Path.Combine("/", "data"), "*.json");

        var entries = browser.Entries;

        Assert.True(browser.ListsFiles);
        Assert.Contains("appsettings.json", entries);
        Assert.Contains("appsettings.Development.json", entries);
        Assert.DoesNotContain("notes.txt", entries);
        Assert.True(IndexOf(entries, "appsettings.json") > IndexOf(entries, "workspace"));
    }

    [Fact]
    public void A_file_entry_resolves_to_its_full_path_and_a_directory_entry_does_not()
    {
        var browser = At(Path.Combine("/", "data"), "*.json");
        var entries = browser.Entries;
        var fileIndex = IndexOf(entries, "appsettings.json");

        Assert.True(browser.IsFileEntry(fileIndex));
        Assert.Equal(
            Path.Combine(browser.CurrentPath, "appsettings.json"),
            browser.FilePathAt(fileIndex));

        var directoryIndex = IndexOf(entries, "workspace");
        Assert.False(browser.IsFileEntry(directoryIndex));
        Assert.Null(browser.FilePathAt(directoryIndex));
    }

    [Fact]
    public void Entering_a_file_entry_does_not_move_the_browser()
    {
        var browser = At(Path.Combine("/", "data"), "*.json");
        var before = browser.CurrentPath;

        browser.Enter(IndexOf(browser.Entries, "appsettings.json"));

        Assert.Equal(before, browser.CurrentPath);
    }

    [Fact]
    public void Entering_a_sub_directory_moves_into_it()
    {
        var browser = At(Path.Combine("/", "data"));

        browser.Enter("workspace");

        Assert.Equal(Path.Combine("/", "data", "workspace"), browser.CurrentPath);
    }

    [Fact]
    public void Entering_the_parent_entry_walks_up()
    {
        var browser = At(Path.Combine("/", "data", "workspace"));

        browser.Enter(DirectoryBrowser.ParentEntry);

        Assert.Equal(Path.Combine("/", "data"), browser.CurrentPath);
    }

    [Fact]
    public void An_entry_that_does_not_exist_leaves_the_browser_where_it_was()
    {
        var browser = At(Path.Combine("/", "data"));

        browser.Enter("nope");

        Assert.Equal(Path.GetFullPath(Path.Combine("/", "data")), browser.CurrentPath);
    }

    [Fact]
    public void An_index_outside_the_list_is_ignored()
    {
        var browser = At(Path.Combine("/", "data"));

        browser.Enter(42);

        Assert.Equal(Path.GetFullPath(Path.Combine("/", "data")), browser.CurrentPath);
        Assert.Null(browser.FilePathAt(42));
    }

    [Fact]
    public void Navigating_to_an_unknown_absolute_path_fails_without_moving()
    {
        var browser = At(Path.Combine("/", "data"));

        Assert.False(browser.TryNavigateTo(Path.Combine("/", "elsewhere")));
        Assert.Equal(Path.GetFullPath(Path.Combine("/", "data")), browser.CurrentPath);

        Assert.True(browser.TryNavigateTo(Path.Combine("/", "data", "output")));
        Assert.Equal(Path.Combine("/", "data", "output"), browser.CurrentPath);
    }
}
