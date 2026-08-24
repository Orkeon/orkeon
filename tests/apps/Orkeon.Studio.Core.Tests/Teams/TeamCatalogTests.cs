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

    [Fact]
    public void The_secret_scan_catches_the_idiomatic_unquoted_yaml_paste_too()
    {
        var source = Path.Combine(_root, "shared");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "crew.yaml"), "api_key: sk-abcdef1234567890\n");
        File.WriteAllText(Path.Combine(source, "clean.yaml"), "api_key: ${ORKEON_Llm__ApiKey}\n");

        Assert.Equal(["crew.yaml"], TeamCatalog.FindInlineSecrets(source));
    }

    [Fact]
    public void A_single_file_candidate_is_named_by_its_file_not_by_a_dot()
    {
        var source = Path.Combine(_root, "revue.yaml");
        Directory.CreateDirectory(_root);
        File.WriteAllText(source, """apiKey: "sk-abcdef1234567890" """);

        Assert.Equal(["revue.yaml"], TeamCatalog.FindInlineSecrets(source));
    }

    [Fact]
    public void Importing_an_ancestor_of_the_teams_root_is_refused_instead_of_recursing()
    {
        var source = Path.Combine(_root, "everything");
        var teams = Path.Combine(source, "teams");
        Directory.CreateDirectory(teams);
        File.WriteAllText(Path.Combine(source, "crew.yaml"), "name: x");

        Assert.Null(TeamCatalog.Import(source, teams));
        Assert.Empty(Directory.EnumerateDirectories(teams));
    }

    [Fact]
    public void A_duplicated_team_renames_its_sidecar_so_the_cards_stay_apart()
    {
        var team = Path.Combine(_root, "veille");
        Directory.CreateDirectory(team);
        TeamCatalog.SaveMetadata(team, new StudioTeamMetadata { Name = "Veille", Profile = "Local" });

        var copy = TeamCatalog.Duplicate(team);

        Assert.NotNull(copy);
        var summary = TeamCatalog.Describe(copy!);
        Assert.NotEqual("Veille", summary.Name);
        Assert.StartsWith("Veille (", summary.Name, StringComparison.Ordinal);
        Assert.Equal("Local", summary.Profile);   // everything else travels unchanged
    }

    [Fact]
    public void The_profile_of_a_target_comes_from_the_sidecar_beside_it()
    {
        var team = Path.Combine(_root, "veille");
        Directory.CreateDirectory(team);
        TeamCatalog.SaveMetadata(team, new StudioTeamMetadata { Name = "Veille", Profile = "Cloud" });
        File.WriteAllText(Path.Combine(team, "crew.yaml"), "name: veille");

        Assert.Equal("Cloud", TeamCatalog.ProfileFor(team));                                  // the folder
        Assert.Equal("Cloud", TeamCatalog.ProfileFor(Path.Combine(team, "crew.yaml")));       // a file inside it
        Assert.Null(TeamCatalog.ProfileFor(Path.Combine(_root, "not-a-team")));               // anything else
    }
}

/// <summary>What the Exécuter screen's team card can honestly say about a target (audit 05/14).</summary>
public sealed class TeamCatalogDescribeTargetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"orkeon-describe-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void A_team_folder_is_described_from_its_sidecar_and_its_agent_files()
    {
        var team = Path.Combine(_root, "veille");
        Directory.CreateDirectory(Path.Combine(team, "agents"));
        File.WriteAllText(Path.Combine(team, "agents", "analyste.yaml"), "role: analyste");
        File.WriteAllText(Path.Combine(team, "agents", "redacteur.yml"), "role: redacteur");
        File.WriteAllText(
            Path.Combine(team, StudioTeamMetadata.FileName),
            """{ "name": "Veille marché", "description": "Surveille le marché chaque matin", "profile": "Quotidien" }""");

        var described = TeamCatalog.DescribeTarget(team);

        Assert.Equal("Veille marché", described.Name);
        Assert.Equal("Surveille le marché chaque matin", described.Description);
        Assert.Equal("Quotidien", described.Profile);
        Assert.Equal(2, described.AgentCount);
    }

    [Fact]
    public void A_single_file_falls_back_to_its_name_and_nothing_ever_throws()
    {
        Assert.Null(TeamCatalog.DescribeTarget("").Name);
        Assert.Null(TeamCatalog.DescribeTarget(Path.Combine(_root, "absent", "crew.yaml")).Description);

        var file = Path.Combine(_root, "solo", "crew.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "goal: x");

        var described = TeamCatalog.DescribeTarget(file);

        Assert.Equal("crew", described.Name);
        Assert.Null(described.AgentCount);
    }
}

/// <summary>The forge working directory must exist before Process.Start refuses it.</summary>
public sealed class EnsureDirectoryTests
{
    [Fact]
    public void The_home_is_created_on_first_use_and_an_existing_one_is_untouched()
    {
        var path = Path.Combine(Path.GetTempPath(), $"orkeon-home-{Guid.NewGuid():N}");
        try
        {
            Assert.False(Directory.Exists(path));
            Assert.Equal(path, TeamCatalog.EnsureDirectory(path));
            Assert.True(Directory.Exists(path));

            // Idempotent: calling again neither throws nor recreates.
            Assert.Equal(path, TeamCatalog.EnsureDirectory(path));
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path);
        }
    }
}

/// <summary>A slug is a folder name: goal-length sentences must not become 200-char directories.</summary>
public sealed class SlugLengthTests
{
    [Fact]
    public void A_goal_length_sentence_is_capped_at_a_word_boundary()
    {
        var goal = "Résumer en une seule exécution les nouveautés d'un site web dont les "
            + "fichiers sont sauvegardés dans le sous-dossier new de c:\\documents sur le PC "
            + "de l'utilisateur et produire un document de synthèse clair et lisible";

        var slug = TeamCatalog.Slugify(goal);

        Assert.True(slug.Length <= TeamCatalog.MaxSlugLength, $"slug too long: {slug.Length}");
        Assert.False(slug.EndsWith('-'));
        Assert.StartsWith("resumer-en-une-seule-execution", slug, StringComparison.Ordinal);
    }

    [Fact]
    public void A_short_name_is_untouched()
    {
        Assert.Equal("veille-matinale", TeamCatalog.Slugify("Veille matinale"));
    }
}
