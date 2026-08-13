using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Core.Tests.Doubles;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// Detection of the three target shapes on declared trees: the file rule mirrors
/// <c>RunCommand.IsYamlConfig</c>, the directory rules mirror the multi-file layout and the
/// <c>crew.ork.ts</c> convention, and a directory matching two shapes fails naming both.
/// </summary>
public sealed class RunTargetDetectorTests
{
    private static readonly string[] BothMarkers = ["/crews/my_crew/agents", "/crews/my_crew/tasks"];

    private static readonly string[] ScriptCandidates =
        ["/crews/scripts/01-first.ork.ts", "/crews/scripts/02-second.ork.ts"];

    private static readonly string[] AmbiguousCandidates =
        ["/crews/mixed/agents", "/crews/mixed/crew.ork.ts"];

    private static string Norm(string path) => path.Replace('\\', '/');

    private static RunTargetDetector Detector(FakeTargetProbe probe) => new(probe);

    [Theory]
    [InlineData("/crews/crew.yaml")]
    [InlineData("/crews/crew.yml")]
    [InlineData("/crews/CREW.YAML")]
    [InlineData("/crews/crew.YmL")]
    public void A_yaml_file_is_a_yaml_target_whatever_its_case(string path)
    {
        var detection = Detector(new FakeTargetProbe().WithFiles(path)).Detect(path);

        Assert.True(detection.IsResolved);
        Assert.Equal(RunTargetKind.YamlFile, detection.Target!.Kind);
        Assert.Equal(RunTargetDialect.Yaml, detection.Target.Dialect);
        Assert.Equal(path, detection.Target.RunPath);
        Assert.False(detection.Target.RequiresDirectoryRunSupport);
    }

    [Theory]
    [InlineData("/crews/crew.ork.ts")]
    [InlineData("/crews/pipeline.ORK.TS")]
    [InlineData("/crews/bundle.js")]
    public void A_script_file_is_a_script_target(string path)
    {
        var detection = Detector(new FakeTargetProbe().WithFiles(path)).Detect(path);

        Assert.True(detection.IsResolved);
        Assert.Equal(RunTargetKind.ScriptFile, detection.Target!.Kind);
        Assert.Equal(RunTargetDialect.Script, detection.Target.Dialect);
        Assert.Equal(path, detection.Target.RunPath);
    }

    [Fact]
    public void A_file_the_cli_does_not_run_is_rejected_with_the_accepted_extensions()
    {
        var detection = Detector(new FakeTargetProbe().WithFiles("/crews/notes.md")).Detect("/crews/notes.md");

        Assert.Equal(RunTargetDetectionStatus.Failed, detection.Status);
        Assert.Equal(RunTargetCodes.UnsupportedExtension, detection.ErrorCode);
        Assert.Contains(".ork.ts", detection.Error!, StringComparison.Ordinal);
        Assert.Contains(".yaml", detection.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("agents")]
    [InlineData("tasks")]
    public void A_directory_with_a_per_entity_sub_folder_is_a_multi_file_crew(string marker)
    {
        var probe = new FakeTargetProbe()
            .WithDirectories("/crews/my_crew", "/crews/my_crew/" + marker)
            .WithFiles("/crews/my_crew/config.yaml");

        var detection = Detector(probe).Detect("/crews/my_crew");

        Assert.True(detection.IsResolved);
        var target = detection.Target!;
        Assert.Equal(RunTargetKind.MultiFileCrewDirectory, target.Kind);
        Assert.Equal(RunTargetDialect.Yaml, target.Dialect);
        Assert.Equal("/crews/my_crew", target.RunPath);
        Assert.Equal("/crews/my_crew/" + marker, Norm(Assert.Single(target.Markers)));
        Assert.True(target.RequiresDirectoryRunSupport);
    }

    [Fact]
    public void A_multi_file_crew_reports_both_marker_folders_when_both_are_present()
    {
        var probe = new FakeTargetProbe()
            .WithDirectories("/crews/my_crew", "/crews/my_crew/agents", "/crews/my_crew/tasks");

        var target = Detector(probe).Detect("/crews/my_crew").Target!;

        Assert.Equal(BothMarkers, target.Markers.Select(Norm));
    }

    [Fact]
    public void A_directory_holding_the_conventional_entry_point_resolves_to_it()
    {
        var probe = new FakeTargetProbe()
            .WithDirectories("/crews/ts")
            .WithFiles("/crews/ts/crew.ork.ts", "/crews/ts/helper.ork.ts");

        var detection = Detector(probe).Detect("/crews/ts");

        Assert.True(detection.IsResolved);
        var target = detection.Target!;
        Assert.Equal(RunTargetKind.ScriptDirectory, target.Kind);
        Assert.Equal("/crews/ts", target.SelectedPath);
        Assert.Equal("/crews/ts/crew.ork.ts", Norm(target.RunPath));
        Assert.False(target.RequiresDirectoryRunSupport);
    }

    [Fact]
    public void A_directory_without_entry_point_offers_its_scripts_as_candidates()
    {
        var probe = new FakeTargetProbe()
            .WithDirectories("/crews/scripts", "/crews/scripts/data")
            .WithFiles(
                "/crews/scripts/02-second.ork.ts",
                "/crews/scripts/01-first.ork.ts",
                "/crews/scripts/notes.md",
                "/crews/scripts/data/nested.ork.ts");

        var detection = Detector(probe).Detect("/crews/scripts");

        Assert.Equal(RunTargetDetectionStatus.NeedsSelection, detection.Status);
        Assert.Null(detection.Target);
        Assert.Equal(ScriptCandidates, detection.Candidates);
    }

    [Fact]
    public void A_candidate_picked_by_the_user_detects_as_a_script_file()
    {
        var probe = new FakeTargetProbe()
            .WithDirectories("/crews/scripts")
            .WithFiles("/crews/scripts/01-first.ork.ts");

        var candidate = Assert.Single(Detector(probe).Detect("/crews/scripts").Candidates);
        var detection = Detector(probe).Detect(candidate);

        Assert.Equal(RunTargetKind.ScriptFile, detection.Target!.Kind);
    }

    [Fact]
    public void A_directory_matching_both_shapes_fails_naming_the_two_candidates()
    {
        var probe = new FakeTargetProbe()
            .WithDirectories("/crews/mixed", "/crews/mixed/agents")
            .WithFiles("/crews/mixed/crew.ork.ts");

        var detection = Detector(probe).Detect("/crews/mixed");

        Assert.Equal(RunTargetDetectionStatus.Failed, detection.Status);
        Assert.Equal(RunTargetCodes.AmbiguousDirectory, detection.ErrorCode);
        Assert.Equal(AmbiguousCandidates, detection.Candidates.Select(Norm));
        Assert.Contains("/crews/mixed/agents", Norm(detection.Error!), StringComparison.Ordinal);
        Assert.Contains("/crews/mixed/crew.ork.ts", Norm(detection.Error!), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RunTargetKind.MultiFileCrewDirectory, "/crews/mixed")]
    [InlineData(RunTargetKind.ScriptDirectory, "/crews/mixed/crew.ork.ts")]
    public void An_ambiguous_directory_resolves_once_the_user_states_which_shape(
        RunTargetKind preferred,
        string expectedRunPath)
    {
        var probe = new FakeTargetProbe()
            .WithDirectories("/crews/mixed", "/crews/mixed/agents")
            .WithFiles("/crews/mixed/crew.ork.ts");

        var detection = Detector(probe).Detect("/crews/mixed", preferred);

        Assert.True(detection.IsResolved);
        Assert.Equal(preferred, detection.Target!.Kind);
        Assert.Equal(expectedRunPath, Norm(detection.Target.RunPath));
    }

    [Fact]
    public void An_empty_directory_is_an_explicit_error()
    {
        var detection = Detector(new FakeTargetProbe().WithDirectories("/crews/empty")).Detect("/crews/empty");

        Assert.Equal(RunTargetDetectionStatus.Failed, detection.Status);
        Assert.Equal(RunTargetCodes.NoCandidate, detection.ErrorCode);
        Assert.Contains("agents/", detection.Error!, StringComparison.Ordinal);
        Assert.Contains("crew.ork.ts", detection.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_directory_holding_only_yaml_files_names_no_candidate()
    {
        var probe = new FakeTargetProbe()
            .WithDirectories("/crews/flat")
            .WithFiles("/crews/flat/crew.yaml", "/crews/flat/agents.yaml", "/crews/flat/tasks.yaml");

        var detection = Detector(probe).Detect("/crews/flat");

        Assert.Equal(RunTargetCodes.NoCandidate, detection.ErrorCode);
        Assert.Empty(detection.Candidates);
    }

    [Fact]
    public void A_missing_path_is_reported_as_such()
    {
        var detection = Detector(new FakeTargetProbe()).Detect("/crews/ghost.yaml");

        Assert.Equal(RunTargetCodes.PathNotFound, detection.ErrorCode);
        Assert.Contains("/crews/ghost.yaml", detection.Error!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_selection_is_reported_before_any_probe(string? path)
    {
        var detection = Detector(new FakeTargetProbe()).Detect(path);

        Assert.Equal(RunTargetCodes.EmptyPath, detection.ErrorCode);
        Assert.Equal(string.Empty, detection.SelectedPath);
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed_off_the_picked_path()
    {
        var probe = new FakeTargetProbe().WithFiles("/crews/crew.yaml");

        var detection = Detector(probe).Detect("  /crews/crew.yaml  ");

        Assert.True(detection.IsResolved);
        Assert.Equal("/crews/crew.yaml", detection.Target!.RunPath);
    }

    [Fact]
    public void A_directory_named_like_a_yaml_file_is_still_read_as_a_directory()
    {
        var probe = new FakeTargetProbe()
            .WithDirectories("/crews/weird.yaml", "/crews/weird.yaml/tasks");

        var detection = Detector(probe).Detect("/crews/weird.yaml");

        Assert.Equal(RunTargetKind.MultiFileCrewDirectory, detection.Target!.Kind);
    }

    [Fact]
    public void The_yaml_rule_matches_the_cli_dispatch_rule()
    {
        Assert.True(RunTargetDetector.IsYamlFile("crew.YAML"));
        Assert.True(RunTargetDetector.IsYamlFile("crew.yml"));
        Assert.False(RunTargetDetector.IsYamlFile("crew.yamlx"));
        Assert.False(RunTargetDetector.IsYamlFile("crew.ork.ts"));

        Assert.True(RunTargetDetector.IsScriptFile("crew.ork.ts"));
        Assert.True(RunTargetDetector.IsScriptFile("bundle.JS"));
        Assert.False(RunTargetDetector.IsScriptFile("crew.yaml"));
    }
}
