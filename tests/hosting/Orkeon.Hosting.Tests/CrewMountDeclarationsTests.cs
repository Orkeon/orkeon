namespace Orkeon.Hosting.Tests;

/// <summary>
/// <see cref="CrewMountDeclarations.Read"/> is the raw pre-read of a crew's <c>mounts:</c>
/// block (VFS-90): what the runner knows of the crew's expectations before any host exists.
/// It must find the block where the loader finds it and never throw — the loader is the
/// authority and reports a broken file properly a moment later.
/// </summary>
public sealed class CrewMountDeclarationsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"orkeon-crew-mounts-{Guid.NewGuid():N}");

    public CrewMountDeclarationsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void A_crew_directory_is_read_from_its_config_yaml()
    {
        var config = Write("config.yaml", """
            name: demo
            goal: Read the block
            mounts:
              - /output
              - 01J9Z3K4M5N6P7Q8R9S0T1V2W3|/data
            """);

        var declarations = CrewMountDeclarations.Read(_dir, isCrewDirectory: true);

        Assert.Equal(config, declarations.SourceFile);
        Assert.Equal(["/output", "01J9Z3K4M5N6P7Q8R9S0T1V2W3|/data"], declarations.Items);
    }

    [Fact]
    public void A_crew_directory_without_config_yaml_falls_back_to_crew_yaml()
    {
        var crew = Write("crew.yaml", "name: demo\nmounts: [/reports]\n");

        var declarations = CrewMountDeclarations.Read(_dir, isCrewDirectory: true);

        Assert.Equal(crew, declarations.SourceFile);
        Assert.Equal(["/reports"], declarations.Items);
    }

    [Fact]
    public void Config_yaml_wins_over_crew_yaml_like_the_loader()
    {
        var config = Write("config.yaml", "name: demo\nmounts: [/a]\n");
        Write("crew.yaml", "name: demo\nmounts: [/b]\n");

        var declarations = CrewMountDeclarations.Read(_dir, isCrewDirectory: true);

        Assert.Equal(config, declarations.SourceFile);
        Assert.Equal(["/a"], declarations.Items);
    }

    [Fact]
    public void A_single_yaml_file_is_read_from_itself()
    {
        var file = Write("team.yaml", "name: demo\nmounts:\n  - /output\n");

        var declarations = CrewMountDeclarations.Read(file, isCrewDirectory: false);

        Assert.Equal(file, declarations.SourceFile);
        Assert.Equal(["/output"], declarations.Items);
    }

    [Fact]
    public void A_file_without_the_block_has_no_items_but_names_its_source()
    {
        var file = Write("team.yaml", "name: demo\ngoal: nothing declared\n");

        var declarations = CrewMountDeclarations.Read(file, isCrewDirectory: false);

        Assert.Equal(file, declarations.SourceFile);
        Assert.Empty(declarations.Items);
    }

    [Fact]
    public void A_script_or_a_missing_file_has_no_declarations()
    {
        var script = Write("team.ork.ts", "export default {};");

        Assert.Same(CrewMountDeclarations.None, CrewMountDeclarations.Read(script, isCrewDirectory: false));
        Assert.Same(CrewMountDeclarations.None, CrewMountDeclarations.Read(Path.Combine(_dir, "absent.yaml"), isCrewDirectory: false));
        Assert.Same(CrewMountDeclarations.None, CrewMountDeclarations.Read(_dir, isCrewDirectory: true));
    }

    [Fact]
    public void A_file_the_probe_cannot_parse_yields_no_items_rather_than_throwing()
    {
        var file = Write("team.yaml", "name: demo\nmounts: [unterminated\n");

        var declarations = CrewMountDeclarations.Read(file, isCrewDirectory: false);

        Assert.Equal(file, declarations.SourceFile);
        Assert.Empty(declarations.Items);
    }

    [Fact]
    public void Blank_items_are_dropped_and_the_others_trimmed()
    {
        var file = Write("team.yaml", "mounts:\n  - '  /output '\n  - ''\n  - ~\n");

        Assert.Equal(["/output"], CrewMountDeclarations.Read(file, isCrewDirectory: false).Items);
    }
}
