using Orkeon.Domain.FileSystem;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Teams;

namespace Orkeon.Studio.Core.Tests.Teams;

/// <summary>
/// The teams directory: folders in, cards out, all I/O tolerant. And the import path's
/// secret scan — a pasted key must be caught before a shared folder becomes a team.
/// </summary>
public sealed class TeamCatalogTests : IDisposable
{
    /// <summary>When a copy entered the teams root (STUDIO-32): a duplicate and an import date their arrival.</summary>
    private static readonly DateTimeOffset AddedOn = new(2026, 9, 24, 11, 0, 0, TimeSpan.Zero);

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

    /// <summary>
    /// The folder is the file's name through the shared slug rule (STUDIO-24), and a name
    /// that keeps no ASCII letter or digit — zh-Hans is one of Studio's five languages —
    /// lands in the team fallback rather than nowhere.
    /// </summary>
    [Theory]
    [InlineData("Ma veille quotidienne.yaml", "ma-veille-quotidienne")]
    [InlineData("Équipe d'été.yaml", "equipe-d-ete")]
    [InlineData("每日监控.yaml", FolderSlug.TeamFallback)]
    public void Importing_a_file_names_its_team_folder_with_the_shared_slug_rule(string fileName, string folder)
    {
        var source = Path.Combine(_root, "incoming", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "name: veille");
        var teams = Path.Combine(_root, "teams");

        var destination = TeamCatalog.Import(source, teams, AddedOn, out var refusal);

        Assert.Null(refusal);
        Assert.Equal(Path.Combine(teams, folder), destination);
    }

    [Fact]
    public void Importing_a_single_file_wraps_it_in_its_own_team_folder()
    {
        var source = Path.Combine(_root, "incoming", "revue-contrats.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "name: revue");
        var teams = Path.Combine(_root, "teams");

        var destination = TeamCatalog.Import(source, teams, AddedOn, out var refusal);

        Assert.Null(refusal);
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

        var first = TeamCatalog.Import(source, teams, AddedOn, out _);
        var second = TeamCatalog.Import(source, teams, AddedOn, out _);

        Assert.Equal(Path.Combine(teams, "revue"), first);
        Assert.Equal(Path.Combine(teams, "revue-2"), second);
    }

    /// <summary>
    /// STUDIO-26, D-07: what already sits where an adoption would write — nothing, a team (the
    /// sidecar, a record naming a session, or a crew the launcher runs), a folder holding none, or a
    /// file. Read before the engine is asked, so the user is told what occupies the name instead of
    /// being shown its refusal.
    /// </summary>
    [Fact]
    public void An_adoption_folder_says_what_occupies_it()
    {
        var teams = Path.Combine(_root, "teams");
        Assert.Equal(TeamFolderOccupant.None, TeamCatalog.OccupantOf(Path.Combine(teams, "libre")));

        var adopted = Path.Combine(teams, "veille");
        TeamCatalog.SaveMetadata(adopted, new StudioTeamMetadata { Name = "Veille" });
        Assert.Equal(TeamFolderOccupant.Team, TeamCatalog.OccupantOf(adopted));

        var promoted = Path.Combine(teams, "promue");
        Directory.CreateDirectory(promoted);
        File.WriteAllText(Path.Combine(promoted, ForgeSessionCatalog.TeamRecordFileName), """{"v":1,"id":"6f1c2a0e-4b7d-4e9a-9f53-1d2c3b4a5e6f"}""");
        Assert.Equal(TeamFolderOccupant.Team, TeamCatalog.OccupantOf(promoted));

        var handMade = Path.Combine(teams, "a-la-main");
        Directory.CreateDirectory(handMade);
        File.WriteAllText(Path.Combine(handMade, "crew.yaml"), "name: a-la-main");
        Assert.Equal(TeamFolderOccupant.Team, TeamCatalog.OccupantOf(handMade));

        var notes = Path.Combine(teams, "notes");
        Directory.CreateDirectory(notes);
        File.WriteAllText(Path.Combine(notes, "idees.txt"), "rien de lançable");
        Assert.Equal(TeamFolderOccupant.Folder, TeamCatalog.OccupantOf(notes));

        var file = Path.Combine(teams, "fichier");
        File.WriteAllText(file, "x");
        Assert.Equal(TeamFolderOccupant.File, TeamCatalog.OccupantOf(file));
    }

    /// <summary>The free sibling of a taken folder: its name suffixed -2, -3…, past folders and files alike.</summary>
    [Fact]
    public void The_free_sibling_of_a_taken_folder_is_suffixed_past_every_entry()
    {
        var teams = Path.Combine(_root, "teams");
        Directory.CreateDirectory(Path.Combine(teams, "veille"));
        Directory.CreateDirectory(Path.Combine(teams, "veille-2"));
        File.WriteAllText(Path.Combine(teams, "veille-3"), "a file");

        Assert.Equal(Path.Combine(teams, "veille-4"), TeamCatalog.FreeSibling(Path.Combine(teams, "veille")));
        Assert.Equal(Path.Combine(teams, "veille-4"), TeamCatalog.FreeSibling(Path.Combine(teams, "veille") + Path.DirectorySeparatorChar));
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

        Assert.Null(TeamCatalog.Import(source, teams, AddedOn, out _));
        Assert.Empty(Directory.EnumerateDirectories(teams));
    }

    // ── STUDIO-12 C1: a folder holding a single-file crew is a team; anything else is refused ──

    [Fact]
    public void Importing_a_folder_holding_a_single_file_crew_makes_a_playable_team()
    {
        // The shape of every examples/ crew: one config.yaml, agents: and tasks: inline.
        var source = Path.Combine(_root, "incoming", "factures");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "config.yaml"), "name: factures\nagents: {}\ntasks: {}\n");
        File.WriteAllText(Path.Combine(source, "README.md"), "how to run\n");
        var teams = Path.Combine(_root, "teams");

        var destination = TeamCatalog.Import(source, teams, AddedOn, out var refusal);

        Assert.Null(refusal);
        Assert.NotNull(destination);
        Assert.True(File.Exists(Path.Combine(destination!, "config.yaml")));

        // What landed is a team the launcher's detector resolves — not a folder it refuses.
        var detection = new Orkeon.Studio.Core.Targets.RunTargetDetector().Detect(destination);
        Assert.True(detection.IsResolved);
        Assert.Equal(Orkeon.Studio.Core.Targets.RunTargetKind.SingleFileCrewDirectory, detection.Target!.Kind);
        Assert.Equal(Path.Combine(destination, "config.yaml"), detection.Target.RunPath);
    }

    [Fact]
    public void Importing_a_folder_that_holds_no_crew_definition_is_refused_before_any_copy()
    {
        // Copied verbatim, this folder used to land in "My teams" as a card nothing could run:
        // the CLI then answered "holds no recognized crew layout".
        var source = Path.Combine(_root, "incoming", "notes");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "README.md"), "nothing runnable here\n");
        var teams = Path.Combine(_root, "teams");

        var destination = TeamCatalog.Import(source, teams, AddedOn, out var refusal);

        Assert.Null(destination);
        Assert.NotNull(refusal);
        Assert.Contains("holds no crew definition", refusal, StringComparison.Ordinal);
        Assert.False(Directory.Exists(teams));
    }

    [Fact]
    public void Importing_a_file_the_cli_does_not_run_is_refused_with_the_detector_message()
    {
        var source = Path.Combine(_root, "incoming", "notes.md");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "not a crew\n");
        var teams = Path.Combine(_root, "teams");

        Assert.Null(TeamCatalog.Import(source, teams, AddedOn, out var refusal));
        Assert.NotNull(refusal);
        Assert.Contains(".ork.ts", refusal, StringComparison.Ordinal);
        Assert.False(Directory.Exists(teams));
    }

    [Fact]
    public void A_duplicated_team_renames_its_sidecar_so_the_cards_stay_apart()
    {
        var team = Path.Combine(_root, "veille");
        Directory.CreateDirectory(team);
        TeamCatalog.SaveMetadata(team, new StudioTeamMetadata { Name = "Veille", Profile = "Local" });

        var copy = TeamCatalog.Duplicate(team, AddedOn);

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

    // ---- STUDIO-16: the display normalisation of names and needs ----------------------

    /// <summary>The page a user pastes into a name or a need: headings, list, code, links.</summary>
    private const string PastedPage = """
        # Extraire les factures fournisseurs déposées en `docs/`, classées par mois et par fournisseur, avec un **contrôle** des doublons

        > Un README entier.

        ## Fonctionnement

        - lit chaque PDF
        - en extrait le [fournisseur](https://exemple.test) et la date

        ```json
        { "montant": 12 }
        ```
        """;

    [Fact]
    public void NormalizeName_keeps_the_first_line_and_cuts_at_a_word_under_64()
    {
        var name = TeamCatalog.NormalizeName(PastedPage);

        // The first line only, its heading marker, code spans and stars gone, cut at a word
        // — never mid-word, never a trailing comma — under the slug's cap.
        Assert.Equal("Extraire les factures fournisseurs déposées en docs/, classées", name);
        Assert.True(name.Length <= TeamCatalog.MaxNameLength, $"too long: {name.Length}");

        // v3 W-07's goal-length title, at the slug's cap now rather than 48.
        Assert.Equal("Veille documentaire", TeamCatalog.NormalizeName("  Veille documentaire  "));
        Assert.Equal(
            "Résumer en un seul passage les nouveautés d'un site, à partir",
            TeamCatalog.NormalizeName(
                "Résumer en un seul passage les nouveautés d'un site, à partir des fichiers enregistrés"));
        // A short name is untouched, trailing punctuation included: only a cut trims it.
        Assert.Equal("Veille docs.", TeamCatalog.NormalizeName("Veille docs."));
    }

    [Fact]
    public void NormalizeName_strips_markdown_and_never_returns_empty()
    {
        Assert.Equal("Tri des factures", TeamCatalog.NormalizeName("## **Tri** des `factures`"));
        Assert.Equal("Veille sur un site", TeamCatalog.NormalizeName("- Veille sur [un site](https://x.test)"));
        Assert.Equal("Rapport hebdo", TeamCatalog.NormalizeName("\n\n_Rapport_ <b>hebdo</b>\nsecond line"));
        Assert.Equal("snake_case stays", TeamCatalog.NormalizeName("snake_case stays"));

        // Nothing left once the markup is gone: the slug stands in, and the slug is never empty.
        Assert.Equal("equipe", TeamCatalog.NormalizeName("### "));
        Assert.Equal("equipe", TeamCatalog.NormalizeName("   "));
        Assert.Equal("equipe", TeamCatalog.NormalizeName(""));
        Assert.False(TeamCatalog.TryNormalizeName("```", out var nothing));
        Assert.Equal("", nothing);
    }

    [Fact]
    public void Summarize_returns_the_first_paragraph_without_markup_under_240()
    {
        // Prose beats the title: the heading is skipped when a paragraph follows it.
        Assert.Equal("Un README entier.", TeamCatalog.Summarize(PastedPage));

        // A single paragraph of 3 000 characters and no line break: cut at a word, ellipsis.
        var longLine = string.Join(' ', Enumerable.Repeat("facture fournisseur", 300));
        var summary = TeamCatalog.Summarize(longLine);
        Assert.True(summary.Length <= TeamCatalog.MaxSummaryLength, $"too long: {summary.Length}");
        Assert.EndsWith("…", summary, StringComparison.Ordinal);
        Assert.EndsWith("fournisseur…", summary, StringComparison.Ordinal);   // a whole word before the ellipsis

        // A heading alone stands in when nothing else says anything; code and rules never do.
        Assert.Equal("Titre seul", TeamCatalog.Summarize("# Titre seul\n\n```\ncode\n```\n\n---"));
        Assert.Equal("", TeamCatalog.Summarize("```\nonly code\n```"));
        // The lines of one paragraph are joined, as Markdown renders them.
        Assert.Equal("Première ligne seconde ligne", TeamCatalog.Summarize("Première ligne\nseconde ligne\n\nautre paragraphe"));
    }

    [Fact]
    public void Describe_exposes_a_summary_next_to_the_whole_description()
    {
        var team = Path.Combine(_root, "factures");
        Directory.CreateDirectory(team);
        TeamCatalog.SaveMetadata(team, new StudioTeamMetadata { Name = "Factures", Description = PastedPage });

        var summary = TeamCatalog.Describe(team);
        var target = TeamCatalog.DescribeTarget(team);

        // Derived at read time, never stored: the sidecar keeps the whole need.
        Assert.Equal(PastedPage, summary.Description);
        Assert.Equal("Un README entier.", summary.Summary);
        Assert.Equal("Un README entier.", target.Summary);
        Assert.DoesNotContain("\"summary\"", File.ReadAllText(Path.Combine(team, StudioTeamMetadata.FileName)), StringComparison.Ordinal);

        Assert.Null(TeamCatalog.Describe(Path.Combine(_root, "no-sidecar")).Summary);
    }

    [Fact]
    public void Import_normalizes_the_name_of_a_hand_written_sidecar()
    {
        var source = Path.Combine(_root, "incoming", "factures");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "crew.yaml"), "name: factures");
        File.WriteAllText(
            Path.Combine(source, StudioTeamMetadata.FileName),
            System.Text.Json.JsonSerializer.Serialize(new StudioTeamMetadata { Name = PastedPage, Description = PastedPage, Profile = "Local" }));
        var teams = Path.Combine(_root, "teams");

        var destination = TeamCatalog.Import(source, teams, AddedOn, out var refusal);

        Assert.Null(refusal);
        Assert.NotNull(destination);
        var imported = TeamCatalog.Describe(destination!);
        Assert.Equal("Extraire les factures fournisseurs déposées en docs/, classées", imported.Name);
        // The copy is rewritten, the source never is; the need travels whole.
        Assert.Equal(PastedPage, imported.Description);
        Assert.Equal("Local", imported.Profile);
        Assert.Equal(PastedPage, TeamCatalog.Describe(source).Name);
    }

    // ── STUDIO-31: archiving is a flag, the folder never moves ──

    private static readonly DateTimeOffset ArchivedOn = new(2026, 9, 24, 9, 15, 0, TimeSpan.Zero);

    /// <summary>A team as the wizard leaves it: sidecar, crew, forge.json, one file of its own.</summary>
    private string AdoptedTeam(string slug, string name)
    {
        var team = Path.Combine(_root, slug);
        TeamCatalog.SaveMetadata(team, new StudioTeamMetadata
        {
            Name = name,
            Description = "Relit la presse du secteur chaque matin.",
            Profile = "Local",
            Mounts = ["./output:/output:rw"],
        });
        File.WriteAllText(Path.Combine(team, "crew.yaml"), $"name: {slug}\n");
        File.WriteAllText(Path.Combine(team, ForgeSessionCatalog.TeamRecordFileName), """{"v":1,"id":"6f1c2a0e-4b7d-4e9a-9f53-1d2c3b4a5e6f"}""");
        File.WriteAllText(Path.Combine(team, "output", "rapport.md"), "# Rapport\n");
        return team;
    }

    private static string[] FilesUnder(string directory) =>
        [.. Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(directory, file))
            .Order(StringComparer.Ordinal)];

    [Fact]
    public void Archiving_moves_nothing_and_flags_the_sidecar_whose_other_fields_stay()
    {
        var team = AdoptedTeam("veille", "Veille");
        var files = FilesUnder(team);

        Assert.True(TeamCatalog.Archive(team, ArchivedOn));

        // Same folder, same files: the path, the session link and the schedule all hold.
        Assert.Equal(files, FilesUnder(team));
        var archived = TeamCatalog.Describe(team);
        Assert.True(archived.IsArchived);
        Assert.Equal(ArchivedOn, archived.ArchivedAt);
        Assert.Equal("Veille", archived.Name);
        Assert.Equal("Local", archived.Profile);
        Assert.Equal(["./output:/output:rw"], archived.Metadata!.Mounts);
        Assert.True(TeamCatalog.DescribeTarget(team).IsArchived);
        Assert.True(TeamCatalog.DescribeTarget(Path.Combine(team, "crew.yaml")).IsArchived);

        Assert.True(TeamCatalog.Restore(team));

        var restored = TeamCatalog.Describe(team);
        Assert.False(restored.IsArchived);
        Assert.Null(restored.ArchivedAt);
        Assert.Equal("Local", restored.Profile);
        Assert.Equal(files, FilesUnder(team));
        // An active team's sidecar does not even name the flag.
        var sidecar = File.ReadAllText(Path.Combine(team, StudioTeamMetadata.FileName));
        Assert.DoesNotContain("archived", sidecar, StringComparison.Ordinal);
    }

    [Fact]
    public void Archiving_a_folder_that_is_not_there_creates_nothing()
    {
        var absent = Path.Combine(_root, "absente");

        Assert.False(TeamCatalog.Archive(absent, ArchivedOn));
        Assert.False(Directory.Exists(absent));
    }

    /// <summary>A sidecar that cannot be read is not overwritten by an archive: what it says would be lost.</summary>
    [Fact]
    public void An_unreadable_sidecar_is_never_overwritten_by_an_archive()
    {
        var team = Path.Combine(_root, "abimee");
        Directory.CreateDirectory(team);
        File.WriteAllText(Path.Combine(team, StudioTeamMetadata.FileName), "{ not json");

        Assert.False(TeamCatalog.Archive(team, ArchivedOn));
        Assert.Equal("{ not json", File.ReadAllText(Path.Combine(team, StudioTeamMetadata.FileName)));
    }

    [Fact]
    public void The_active_list_ignores_the_archives_and_the_dot_folders()
    {
        AdoptedTeam("veille", "Veille");
        var archived = AdoptedTeam("synthese", "Synthèse");
        TeamCatalog.Archive(archived, ArchivedOn);
        // What an engine's workspace state looks like: never a team, whatever it holds.
        var state = Path.Combine(_root, ".orkeon");
        TeamCatalog.SaveMetadata(state, new StudioTeamMetadata { Name = "State" });
        Directory.CreateDirectory(Path.Combine(_root, ".git"));

        Assert.Equal(["veille"], TeamCatalog.List(_root).Select(team => team.Slug));
        Assert.Equal(["veille"], TeamCatalog.List(_root, TeamListFilter.Active).Select(team => team.Slug));
        Assert.Equal(["synthese"], TeamCatalog.List(_root, TeamListFilter.Archived).Select(team => team.Slug));
        Assert.Equal(["synthese", "veille"], TeamCatalog.List(_root, TeamListFilter.All).Select(team => team.Slug));
    }

    [Fact]
    public void The_list_ignores_the_hidden_and_system_folders_of_windows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Hidden and System are Windows attributes.");

        AdoptedTeam("veille", "Veille");
        var hidden = AdoptedTeam("cachee", "Cachée");
        File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        var system = AdoptedTeam("systeme", "Système");
        File.SetAttributes(system, File.GetAttributes(system) | FileAttributes.System);

        Assert.Equal(["veille"], TeamCatalog.List(_root, TeamListFilter.All).Select(team => team.Slug));
    }

    [Fact]
    public void Duplicating_an_archived_team_gives_an_active_one()
    {
        var team = AdoptedTeam("veille", "Veille");
        TeamCatalog.Archive(team, ArchivedOn);

        var copy = TeamCatalog.Duplicate(team, AddedOn);

        Assert.NotNull(copy);
        var duplicated = TeamCatalog.Describe(copy!);
        Assert.False(duplicated.IsArchived);
        Assert.Null(duplicated.ArchivedAt);
        Assert.Equal("Local", duplicated.Profile);
        // The original stays where it was: archived.
        Assert.True(TeamCatalog.Describe(team).IsArchived);
    }

    /// <summary>
    /// A copy that fails halfway used to stay behind as a partial folder the list showed as a team.
    /// A file held open without sharing is what makes a copy fail on every platform.
    /// </summary>
    [Fact]
    public void A_failed_duplicate_leaves_no_folder_behind()
    {
        var team = AdoptedTeam("veille", "Veille");

        string? copy;
        using (new FileStream(Path.Combine(team, "crew.yaml"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            copy = TeamCatalog.Duplicate(team, AddedOn);

        Assert.Null(copy);
        Assert.Equal(["veille"], Directory.EnumerateDirectories(_root).Select(Path.GetFileName));
    }

    [Fact]
    public void A_failed_import_leaves_no_folder_behind()
    {
        var source = Path.Combine(_root, "incoming", "revue");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "crew.yaml"), "name: revue");
        File.WriteAllText(Path.Combine(source, "notes.md"), "held open");
        var teams = Path.Combine(_root, "teams");
        Directory.CreateDirectory(teams);

        string? imported;
        using (new FileStream(Path.Combine(source, "notes.md"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            imported = TeamCatalog.Import(source, teams, AddedOn, out _);

        Assert.Null(imported);
        Assert.Empty(Directory.EnumerateFileSystemEntries(teams));
    }

    /// <summary>D-04: an export carries the flag as the team has it; the import of anything lands active.</summary>
    [Fact]
    public void An_export_keeps_the_flag_and_an_import_clears_it()
    {
        var team = AdoptedTeam("veille", "Veille");
        TeamCatalog.Archive(team, ArchivedOn);
        var shared = Path.Combine(_root, "partage");
        Directory.CreateDirectory(shared);

        var exported = TeamCatalog.ExportTo(team, shared);
        var imported = TeamCatalog.Import(exported!, Path.Combine(_root, "ailleurs"), AddedOn, out var refusal);

        Assert.True(TeamCatalog.Describe(exported!).IsArchived);
        Assert.Null(refusal);
        Assert.NotNull(imported);
        Assert.False(TeamCatalog.Describe(imported!).IsArchived);
        Assert.Null(TeamCatalog.Describe(imported!).ArchivedAt);
    }

    /// <summary>
    /// D-05, completed by STUDIO-32: the last activity is the most recent of four dates, read at load —
    /// the last run Studio recorded in the sidecar, the last entry of the launch history, the promotion
    /// forge.json records, and the arrival of a copy (a duplicate, an import).
    /// </summary>
    [Theory]
    [InlineData(4, 1, 2, 3)]
    [InlineData(1, 4, 2, 3)]
    [InlineData(1, 2, 4, 3)]
    [InlineData(1, 2, 3, 4)]
    public void The_last_activity_is_the_most_recent_of_the_four_dates(int lastRunDay, int historyDay, int promotedDay, int addedDay)
    {
        static DateTimeOffset On(int day) => new(2026, 9, day, 8, 0, 0, TimeSpan.Zero);
        var team = Path.Combine(_root, "veille");
        TeamCatalog.SaveMetadata(team, new StudioTeamMetadata { Name = "Veille", LastRunAt = On(lastRunDay), AddedAt = On(addedDay) });
        File.WriteAllText(
            Path.Combine(team, ForgeSessionCatalog.TeamRecordFileName),
            $$"""{"v":1,"id":"6f1c2a0e-4b7d-4e9a-9f53-1d2c3b4a5e6f","promotedAt":"2026-09-0{{promotedDay}}T08:00:00Z"}""");

        var summary = TeamCatalog.Describe(team);

        Assert.Equal(On(lastRunDay), summary.LastRunAt);
        Assert.Equal(On(promotedDay), summary.PromotedAt);
        Assert.Equal(On(addedDay), summary.AddedAt);
        Assert.Equal(On(4), summary.LastActivity(On(historyDay)));
    }

    [Fact]
    public void A_team_with_no_date_at_all_has_no_last_activity()
    {
        var team = Path.Combine(_root, "muette");
        Directory.CreateDirectory(team);
        File.WriteAllText(Path.Combine(team, ForgeSessionCatalog.TeamRecordFileName), """{"v":1,"promotedAt":"not a date"}""");

        var summary = TeamCatalog.Describe(team);

        Assert.Null(summary.PromotedAt);
        Assert.Null(summary.LastActivity());
        Assert.Equal(ArchivedOn, summary.LastActivity(ArchivedOn));
    }

    /// <summary>
    /// D-05: a real run stamps <c>lastRunAt</c> into an adopted team right under the teams root — the
    /// folder or a file inside it — and nowhere else: a folder without its sidecar gains none, and a
    /// team outside the root is left as it was.
    /// </summary>
    [Fact]
    public void A_run_stamps_only_an_adopted_team_under_the_teams_root()
    {
        var teams = Path.Combine(_root, "teams");
        var team = Path.Combine(teams, "veille");
        TeamCatalog.SaveMetadata(team, new StudioTeamMetadata { Name = "Veille", Profile = "Local" });
        File.WriteAllText(Path.Combine(team, "crew.yaml"), "name: veille\n");
        var bare = Path.Combine(teams, "nue");
        Directory.CreateDirectory(bare);
        var elsewhere = Path.Combine(_root, "ailleurs", "veille");
        TeamCatalog.SaveMetadata(elsewhere, new StudioTeamMetadata { Name = "Ailleurs" });

        Assert.True(TeamCatalog.RecordRun(teams, team, ArchivedOn));
        Assert.Equal(ArchivedOn, TeamCatalog.Describe(team).LastRunAt);
        Assert.Equal("Local", TeamCatalog.Describe(team).Profile);

        var later = ArchivedOn.AddHours(2);
        Assert.True(TeamCatalog.RecordRun(teams, Path.Combine(team, "crew.yaml"), later));
        Assert.Equal(later, TeamCatalog.Describe(team).LastRunAt);

        Assert.False(TeamCatalog.RecordRun(teams, bare, ArchivedOn));
        Assert.False(File.Exists(Path.Combine(bare, StudioTeamMetadata.FileName)));
        Assert.False(TeamCatalog.RecordRun(teams, elsewhere, ArchivedOn));
        Assert.Null(TeamCatalog.Describe(elsewhere).LastRunAt);
        Assert.False(TeamCatalog.RecordRun(teams, Path.Combine(teams, "absente"), ArchivedOn));
        Assert.False(Directory.Exists(Path.Combine(teams, "absente")));
    }

    // ── STUDIO-32: a copy's arrival is its activity ──

    /// <summary>
    /// A copy of an old team is not an old team. Carrying its original's last run — and its
    /// forge.json promotion — it sorted at the bottom of My teams and was proposed for archiving the
    /// moment it was made. The copy forgets the last run, which is not its own, and dates its
    /// arrival: the most recent of its dates from then on.
    /// </summary>
    [Fact]
    public void A_duplicate_dates_its_arrival_and_forgets_its_originals_last_run()
    {
        var team = AdoptedTeam("veille", "Veille");
        TeamCatalog.UpdateMetadata(team, current => current with { LastRunAt = ArchivedOn.AddDays(-200) });
        File.WriteAllText(
            Path.Combine(team, ForgeSessionCatalog.TeamRecordFileName),
            """{"v":1,"id":"6f1c2a0e-4b7d-4e9a-9f53-1d2c3b4a5e6f","promotedAt":"2025-01-10T08:00:00Z"}""");

        var copy = TeamCatalog.Describe(TeamCatalog.Duplicate(team, AddedOn)!);

        Assert.Null(copy.LastRunAt);
        Assert.Equal(AddedOn, copy.AddedAt);
        Assert.Equal(AddedOn, copy.LastActivity());
        Assert.Equal("Local", copy.Profile);
        // The original keeps what it had, and gains nothing.
        var original = TeamCatalog.Describe(team);
        Assert.Equal(ArchivedOn.AddDays(-200), original.LastRunAt);
        Assert.Null(original.AddedAt);
    }

    /// <summary>An import is a copy too, whatever dates the sidecar it came with carries.</summary>
    [Fact]
    public void An_import_dates_its_arrival_whatever_the_sidecar_it_came_with_says()
    {
        var source = Path.Combine(_root, "incoming", "revue");
        TeamCatalog.SaveMetadata(source, new StudioTeamMetadata
        {
            Name = "Revue",
            LastRunAt = ArchivedOn.AddDays(-300),
            AddedAt = ArchivedOn.AddDays(-400),
        });
        File.WriteAllText(Path.Combine(source, "crew.yaml"), "name: revue");

        var imported = TeamCatalog.Describe(TeamCatalog.Import(source, Path.Combine(_root, "teams"), AddedOn, out _)!);

        Assert.Equal("Revue", imported.Name);
        Assert.Null(imported.LastRunAt);
        Assert.Equal(AddedOn, imported.AddedAt);
        Assert.Equal(AddedOn, imported.LastActivity());
        // The source is read, never written.
        Assert.Equal(ArchivedOn.AddDays(-300), TeamCatalog.Describe(source).LastRunAt);
    }

    /// <summary>
    /// A copy that came without a sidecar — a crew folder shared by hand, a single file — gains a
    /// minimal one: its arrival would otherwise go unrecorded, and the team would sort as never used.
    /// </summary>
    [Fact]
    public void A_copy_without_a_sidecar_gains_one_that_dates_its_arrival()
    {
        var folder = Path.Combine(_root, "incoming", "revue");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "crew.yaml"), "name: revue");
        var file = Path.Combine(_root, "incoming", "tri.yaml");
        File.WriteAllText(file, "name: tri");
        var bare = Path.Combine(_root, "nue");
        Directory.CreateDirectory(bare);
        File.WriteAllText(Path.Combine(bare, "crew.yaml"), "name: nue");
        var teams = Path.Combine(_root, "teams");

        var fromFolder = TeamCatalog.Describe(TeamCatalog.Import(folder, teams, AddedOn, out _)!);
        var fromFile = TeamCatalog.Describe(TeamCatalog.Import(file, teams, AddedOn, out _)!);
        var duplicated = TeamCatalog.Describe(TeamCatalog.Duplicate(bare, AddedOn.AddHours(1))!);

        Assert.Equal(AddedOn, fromFolder.AddedAt);
        Assert.Equal("revue", fromFolder.Name);
        Assert.Equal(AddedOn, fromFile.AddedAt);
        Assert.Equal(AddedOn.AddHours(1), duplicated.AddedAt);
        Assert.Equal("nue-copy", duplicated.Name);
        // Nothing is written where the copies came from.
        Assert.False(File.Exists(Path.Combine(folder, StudioTeamMetadata.FileName)));
        Assert.False(File.Exists(Path.Combine(bare, StudioTeamMetadata.FileName)));
    }

    /// <summary>D-02: a writer changes the fields it owns and keeps every other one.</summary>
    [Fact]
    public void UpdateMetadata_merges_into_the_sidecar_instead_of_rebuilding_it()
    {
        var team = Path.Combine(_root, "veille");
        TeamCatalog.SaveMetadata(team, new StudioTeamMetadata { Name = "Veille", LastRunAt = ArchivedOn });
        TeamCatalog.Archive(team, ArchivedOn);

        TeamCatalog.UpdateMetadata(team, current => current with { Name = "Veille renommée", Profile = "Cloud" });

        var summary = TeamCatalog.Describe(team);
        Assert.Equal("Veille renommée", summary.Name);
        Assert.Equal("Cloud", summary.Profile);
        Assert.True(summary.IsArchived);
        Assert.Equal(ArchivedOn, summary.ArchivedAt);
        Assert.Equal(ArchivedOn, summary.LastRunAt);
    }
}

/// <summary>What the Run screen's team card can honestly say about a target (audit 05/14).</summary>
public sealed class TeamCatalogDescribeTargetTests : IDisposable
{
    /// <summary>When a copy entered the teams root (STUDIO-32): a duplicate and an import date their arrival.</summary>
    private static readonly DateTimeOffset AddedOn = new(2026, 9, 24, 11, 0, 0, TimeSpan.Zero);

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

    /// <summary>
    /// FORGE-09: a team « Modify » can reopen without a session is one whose <c>crew/</c> the
    /// engine can read back — a YAML settings file, no script. A folder with agents but no
    /// <c>crew/</c>, a script crew, or a bare sidecar cannot be rebuilt from.
    /// </summary>
    [Fact]
    public void A_team_says_whether_its_crew_can_be_read_back()
    {
        var yaml = Path.Combine(_root, "yaml");
        Directory.CreateDirectory(Path.Combine(yaml, "crew"));
        File.WriteAllText(Path.Combine(yaml, "crew", "config.yaml"), "name: veille\n");
        Assert.True(TeamCatalog.Describe(yaml).HasYamlCrew);

        var fallback = Path.Combine(_root, "fallback");
        Directory.CreateDirectory(Path.Combine(fallback, "crew"));
        File.WriteAllText(Path.Combine(fallback, "crew", "crew.yaml"), "name: veille\n");
        Assert.True(TeamCatalog.Describe(fallback).HasYamlCrew);

        var script = Path.Combine(_root, "script");
        Directory.CreateDirectory(Path.Combine(script, "crew"));
        File.WriteAllText(Path.Combine(script, "crew", "config.yaml"), "name: veille\n");
        File.WriteAllText(Path.Combine(script, "crew", "crew.ork.ts"), "// crew");
        Assert.False(TeamCatalog.Describe(script).HasYamlCrew);

        var flat = Path.Combine(_root, "flat");
        Directory.CreateDirectory(Path.Combine(flat, "agents"));
        File.WriteAllText(Path.Combine(flat, "agents", "a.yaml"), "role: a\n");
        Assert.False(TeamCatalog.Describe(flat).HasYamlCrew);
        Assert.False(TeamCatalog.Describe(Path.Combine(_root, "absent")).HasYamlCrew);
    }

    /// <summary>
    /// STUDIO-25: a team names its workshop session by the id its <c>forge.json</c> carries — the
    /// only link « Modify » needs to know exists; which session it is, rule R decides in the
    /// engine. A copy carries the same id as its original until the engine rewrites it, and a
    /// folder without the record carries none.
    /// </summary>
    [Fact]
    public void A_team_names_its_session_by_the_id_its_forge_json_carries()
    {
        var team = Path.Combine(_root, "veille");
        Directory.CreateDirectory(team);
        Assert.Null(TeamCatalog.Describe(team).ForgeSessionId);

        File.WriteAllText(
            Path.Combine(team, ForgeSessionCatalog.TeamRecordFileName),
            """{"v":1,"id":"6f1c2a0e-4b7d-4e9a-9f53-1d2c3b4a5e6f","slug":"veille"}""");
        Assert.Equal(Guid.Parse("6f1c2a0e-4b7d-4e9a-9f53-1d2c3b4a5e6f"), TeamCatalog.Describe(team).ForgeSessionId);

        var copy = TeamCatalog.Duplicate(team, AddedOn);
        Assert.NotNull(copy);
        Assert.Equal(TeamCatalog.Describe(team).ForgeSessionId, TeamCatalog.Describe(copy!).ForgeSessionId);
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
