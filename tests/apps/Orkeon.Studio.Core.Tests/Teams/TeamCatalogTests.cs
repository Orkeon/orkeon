using Orkeon.Studio.Core.Teams;

namespace Orkeon.Studio.Core.Tests.Teams;

/// <summary>
/// The teams directory: folders in, cards out, all I/O tolerant. And the import path's
/// secret scan — a pasted key must be caught before a shared folder becomes a team.
/// </summary>
public sealed class TeamCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"orkeon-catalog-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void A_missing_root_lists_as_empty_and_a_corrupt_sidecar_degrades_to_a_plain_entry()
    {
        Assert.Empty(TeamCatalog.List(_root));

        var team = Path.Combine(_root, "veille");
        Directory.CreateDirectory(team);
        File.WriteAllText(Path.Combine(team, StudioTeamMetadata.FileName), "{ not json");

        var summary = Assert.Single(TeamCatalog.List(_root));
        Assert.Equal("veille", summary.Name);
        Assert.False(summary.HasMetadata);
    }

    [Fact]
    public void Slugify_speaks_lowercase_ascii_with_dashes()
    {
        Assert.Equal("ma-veille-quotidienne", TeamCatalog.Slugify("Ma veille quotidienne"));
        Assert.Equal("equipe", TeamCatalog.Slugify("Équipe"));
        Assert.Equal("equipe", TeamCatalog.Slugify("???"));
    }

    [Fact]
    public void Importing_a_single_file_wraps_it_in_its_own_team_folder()
    {
        var source = Path.Combine(_root, "incoming", "revue-contrats.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "name: revue");
        var teams = Path.Combine(_root, "teams");

        var destination = TeamCatalog.Import(source, teams);

        Assert.NotNull(destination);
        Assert.Equal(Path.Combine(teams, "revue-contrats"), destination);
        Assert.True(File.Exists(Path.Combine(destination!, "revue-contrats.yaml")));
    }

    [Fact]
    public void Importing_the_same_folder_twice_never_overwrites()
    {
        var source = Path.Combine(_root, "incoming", "revue");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "crew.yaml"), "name: revue");
        var teams = Path.Combine(_root, "teams");

        var first = TeamCatalog.Import(source, teams);
        var second = TeamCatalog.Import(source, teams);

        Assert.Equal(Path.Combine(teams, "revue"), first);
        Assert.Equal(Path.Combine(teams, "revue-2"), second);
    }

    [Fact]
    public void The_secret_scan_names_a_pasted_key_but_lets_environment_references_pass()
    {
        var source = Path.Combine(_root, "shared");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "crew.yaml"), """apiKey: "sk-abcdef1234567890" """);
        File.WriteAllText(Path.Combine(source, "clean.yaml"), """api_key: "${ORKEON_Llm__ApiKey}" """);
        File.WriteAllText(Path.Combine(source, "notes.txt"), """token: "totally-a-secret-1234" """);

        var offending = TeamCatalog.FindInlineSecrets(source);

        // Only the definition file formats are scanned, and only real values offend.
        Assert.Equal(["crew.yaml"], offending);
    }
}
