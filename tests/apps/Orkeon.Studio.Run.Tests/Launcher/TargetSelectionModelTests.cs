using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Run.Launcher;
using Orkeon.Studio.Run.Tests.Doubles;

namespace Orkeon.Studio.Run.Tests.Launcher;

/// <summary>
/// The target picker over a declared file tree: the three shapes of SPEC §5.1, the two
/// answers the user has to give (which script, which shape), and the P1 prerequisite a
/// multi-file crew directory carries.
/// </summary>
public class TargetSelectionModelTests
{
    private static TargetSelectionModel CreateModel(FakeTargetProbe probe) =>
        new(new RunTargetDetector(probe));

    [Fact]
    public void Yaml_file_resolves_to_a_yaml_target()
    {
        var model = CreateModel(new FakeTargetProbe().WithFiles("/crews/demo/crew.yaml"));

        model.Select("/crews/demo/crew.yaml");

        Assert.Equal(TargetSelectionState.Resolved, model.State);
        Assert.Equal(RunTargetKind.YamlFile, model.Target!.Kind);
        Assert.Equal(RunTargetDialect.Yaml, model.Target.Dialect);
        Assert.Null(model.FrameworkRequirement);
    }

    [Fact]
    public void Script_directory_runs_the_conventional_entry_point()
    {
        var probe = new FakeTargetProbe()
            .WithDirectories("/crews/script")
            .WithFiles("/crews/script/crew.ork.ts");
        var model = CreateModel(probe);

        model.Select("/crews/script");

        Assert.Equal(RunTargetKind.ScriptDirectory, model.Target!.Kind);
        Assert.Equal("/crews/script/crew.ork.ts", model.Target.RunPath.Replace('\\', '/'));
        Assert.Contains("crew.ork.ts", model.ShapeDescription, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_directory_without_entry_point_offers_its_scripts()
    {
        var probe = new FakeTargetProbe()
            .WithDirectories("/crews/many")
            .WithFiles("/crews/many/alpha.ork.ts", "/crews/many/beta.ork.ts");
        var model = CreateModel(probe);

        model.Select("/crews/many");

        Assert.Equal(TargetSelectionState.NeedsSelection, model.State);
        Assert.Equal(["/crews/many/alpha.ork.ts", "/crews/many/beta.ork.ts"], model.Candidates);
        Assert.Null(model.Target);
    }

    [Fact]
    public void Choosing_a_candidate_resolves_it_as_a_script_file()
    {
        var probe = new FakeTargetProbe()
            .WithDirectories("/crews/many")
            .WithFiles("/crews/many/alpha.ork.ts", "/crews/many/beta.ork.ts");
        var model = CreateModel(probe);
        model.Select("/crews/many");

        model.SelectCandidate("/crews/many/beta.ork.ts");

        Assert.Equal(TargetSelectionState.Resolved, model.State);
        Assert.Equal(RunTargetKind.ScriptFile, model.Target!.Kind);
        Assert.Equal("/crews/many/beta.ork.ts", model.Target.RunPath);
    }

    [Fact]
    public void Ambiguous_directory_fails_naming_both_candidates()
    {
        var probe = new FakeTargetProbe()
            .WithDirectories("/crews/both", "/crews/both/agents")
            .WithFiles("/crews/both/crew.ork.ts");
        var model = CreateModel(probe);

        model.Select("/crews/both");

        Assert.Equal(TargetSelectionState.Failed, model.State);
        Assert.True(model.NeedsShapeChoice);
        Assert.Equal(RunTargetCodes.AmbiguousDirectory, model.ErrorCode);
        Assert.Contains("agents", model.Error!, StringComparison.Ordinal);
        Assert.Contains("crew.ork.ts", model.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Answering_the_ambiguity_resolves_the_chosen_shape()
    {
        var probe = new FakeTargetProbe()
            .WithDirectories("/crews/both", "/crews/both/agents")
            .WithFiles("/crews/both/crew.ork.ts");
        var model = CreateModel(probe);
        model.Select("/crews/both");

        model.ResolveShape(RunTargetKind.ScriptDirectory);

        Assert.Equal(TargetSelectionState.Resolved, model.State);
        Assert.Equal(RunTargetKind.ScriptDirectory, model.Target!.Kind);
    }

    [Fact]
    public void Multi_file_directory_resolves_and_states_the_version_it_needs()
    {
        var probe = new FakeTargetProbe().WithDirectories("/crews/multi", "/crews/multi/agents");
        var model = CreateModel(probe);

        model.Select("/crews/multi");

        // The point of SPEC §6: a version requirement, not the CLI's raw "unsupported file" error.
        Assert.Equal(TargetSelectionState.Resolved, model.State);
        Assert.Null(model.Error);
        Assert.NotNull(model.FrameworkRequirement);
        Assert.Contains(
            $"Requires Orkeon >= {DirectoryRunSupport.MinimumCliVersion}",
            model.FrameworkRequirement!,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_path_fails_with_the_detector_message()
    {
        var model = CreateModel(new FakeTargetProbe());

        model.Select("/nowhere/crew.yaml");

        Assert.Equal(TargetSelectionState.Failed, model.State);
        Assert.Equal(RunTargetCodes.PathNotFound, model.ErrorCode);
        Assert.False(model.NeedsShapeChoice);
    }

    [Fact]
    public void Nothing_selected_is_the_empty_state()
    {
        var model = CreateModel(new FakeTargetProbe().WithFiles("/crews/demo/crew.yaml"));
        model.Select("/crews/demo/crew.yaml");

        model.Clear();

        Assert.Equal(TargetSelectionState.Empty, model.State);
        Assert.False(model.IsResolved);
        Assert.Empty(model.Candidates);
    }
}
