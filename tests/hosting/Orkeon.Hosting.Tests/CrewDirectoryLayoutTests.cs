namespace Orkeon.Hosting.Tests;

/// <summary>
/// Contract tests for <see cref="CrewDirectoryLayout"/>, the classifier every runner uses to
/// decide whether a crew target is a multi-file directory. Fixtures are real temp directories:
/// the classifier deliberately probes the physical disk (it runs before any VFS mount exists).
/// </summary>
public sealed class CrewDirectoryLayoutTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ork-crewdir-" + Guid.NewGuid().ToString("N"));

    public CrewDirectoryLayoutTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private string NewDirectory(string name)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "# fixture\n");
    }

    [Theory]
    [InlineData("agents")]
    [InlineData("tasks")]
    public void Per_entity_directory_is_a_crew_directory(string entityFolder)
    {
        var dir = NewDirectory("per-entity-" + entityFolder);
        Touch(Path.Combine(dir, "config.yaml"));
        Directory.CreateDirectory(Path.Combine(dir, entityFolder));

        var inspection = CrewDirectoryLayout.Inspect(dir);

        Assert.True(inspection.IsCrewDirectory);
        Assert.Null(inspection.Error);
    }

    [Fact]
    public void Flat_triplet_directory_is_a_crew_directory()
    {
        var dir = NewDirectory("flat");
        Touch(Path.Combine(dir, "crew.yaml"));
        Touch(Path.Combine(dir, "agents.yaml"));
        Touch(Path.Combine(dir, "tasks.yaml"));

        var inspection = CrewDirectoryLayout.Inspect(dir);

        Assert.True(inspection.IsCrewDirectory);
    }

    [Fact]
    public void Partial_flat_triplet_is_not_a_crew_directory()
    {
        // crew.yaml alone is the single-file form: the user must point at the file.
        var dir = NewDirectory("partial-flat");
        Touch(Path.Combine(dir, "crew.yaml"));

        var inspection = CrewDirectoryLayout.Inspect(dir);

        Assert.False(inspection.IsCrewDirectory);
        Assert.Contains("no recognized crew layout", inspection.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_path_is_reported_as_not_a_directory_without_a_diagnostic()
    {
        // The caller keeps its extension dispatch — a file must not produce an error here.
        var file = Path.Combine(_root, "crew.yaml");
        Touch(file);

        var inspection = CrewDirectoryLayout.Inspect(file);

        Assert.False(inspection.IsCrewDirectory);
        Assert.Null(inspection.Error);
    }

    [Fact]
    public void A_missing_path_is_reported_as_not_a_directory_without_a_diagnostic()
    {
        var inspection = CrewDirectoryLayout.Inspect(Path.Combine(_root, "does-not-exist"));

        Assert.False(inspection.IsCrewDirectory);
        Assert.Null(inspection.Error);
    }

    [Fact]
    public void A_trailing_separator_does_not_change_the_verdict()
    {
        var dir = NewDirectory("trailing");
        Touch(Path.Combine(dir, "config.yaml"));
        Directory.CreateDirectory(Path.Combine(dir, "agents"));

        var inspection = CrewDirectoryLayout.Inspect(dir + Path.DirectorySeparatorChar);

        Assert.True(inspection.IsCrewDirectory);
    }

    [Theory]
    [InlineData("crew.ork.ts")]
    [InlineData("crew.ork.js")]
    public void Yaml_layout_next_to_a_script_is_ambiguous_and_names_both_candidates(string scriptName)
    {
        var dir = NewDirectory("ambiguous-" + scriptName);
        Touch(Path.Combine(dir, "config.yaml"));
        Directory.CreateDirectory(Path.Combine(dir, "agents"));
        Touch(Path.Combine(dir, scriptName));

        var inspection = CrewDirectoryLayout.Inspect(dir);

        Assert.False(inspection.IsCrewDirectory);
        Assert.Contains("Ambiguous crew directory", inspection.Error, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(dir, "agents"), inspection.Error, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(dir, scriptName), inspection.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Flat_layout_next_to_a_script_is_ambiguous_and_names_the_flat_files()
    {
        var dir = NewDirectory("ambiguous-flat");
        Touch(Path.Combine(dir, "crew.yaml"));
        Touch(Path.Combine(dir, "agents.yaml"));
        Touch(Path.Combine(dir, "tasks.yaml"));
        Touch(Path.Combine(dir, "crew.ork.ts"));

        var inspection = CrewDirectoryLayout.Inspect(dir);

        Assert.False(inspection.IsCrewDirectory);
        Assert.Contains(Path.Combine(dir, "agents.yaml"), inspection.Error, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(dir, "crew.ork.ts"), inspection.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_directory_lists_every_layout_that_was_searched()
    {
        var dir = NewDirectory("empty");

        var inspection = CrewDirectoryLayout.Inspect(dir);

        Assert.False(inspection.IsCrewDirectory);
        Assert.Contains("'agents/'", inspection.Error, StringComparison.Ordinal);
        Assert.Contains("'tasks/'", inspection.Error, StringComparison.Ordinal);
        Assert.Contains("crew.yaml + agents.yaml + tasks.yaml", inspection.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_directory_holding_only_a_script_points_at_that_script()
    {
        var dir = NewDirectory("script-only");
        Touch(Path.Combine(dir, "crew.ork.ts"));

        var inspection = CrewDirectoryLayout.Inspect(dir);

        Assert.False(inspection.IsCrewDirectory);
        Assert.Contains("pass that file directly", inspection.Error, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(dir, "crew.ork.ts"), inspection.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_path_is_rejected()
    {
        Assert.ThrowsAny<ArgumentException>(() => CrewDirectoryLayout.Inspect(" "));
    }
}
