using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// The same detection, run on the real disk against the repository's own example crews —
/// the declared-tree tests would keep passing if the shapes drifted from what is shipped.
/// The multi-file layout has no example in the repo yet, so those cases build a real tree
/// in a temporary directory.
/// </summary>
public sealed class RunTargetFixtureTests : IDisposable
{
    private readonly RunTargetDetector _detector = new(PhysicalTargetProbe.Instance);
    private readonly string _temporaryRoot =
        Path.Combine(Path.GetTempPath(), "orkeon-studio-targets-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot))
            Directory.Delete(_temporaryRoot, recursive: true);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Orkeon.sln")))
            directory = directory.Parent;

        Assert.True(directory is not null, $"Could not locate the repo root above {AppContext.BaseDirectory}.");
        return directory!.FullName;
    }

    private static string ExamplePath(params string[] segments) =>
        Path.Combine([RepositoryRoot(), "examples", .. segments]);

    private string CreateTree(params string[] relativeDirectories)
    {
        Directory.CreateDirectory(_temporaryRoot);
        foreach (var relative in relativeDirectories)
            Directory.CreateDirectory(Path.Combine(_temporaryRoot, relative));

        return _temporaryRoot;
    }

    [Fact]
    public void The_yaml_crew_of_the_rag_example_is_a_yaml_file_target()
    {
        var path = ExamplePath("rag", "crew-yaml", "crew.yaml");
        Assert.True(File.Exists(path), $"Fixture moved: {path}");

        var target = _detector.Detect(path).Target;

        Assert.NotNull(target);
        Assert.Equal(RunTargetKind.YamlFile, target.Kind);
        Assert.Equal(path, target.RunPath);
    }

    [Fact]
    public void A_scripting_example_file_is_a_script_file_target()
    {
        var path = ExamplePath("scripting", "01-hello-world.ork.ts");
        Assert.True(File.Exists(path), $"Fixture moved: {path}");

        var target = _detector.Detect(path).Target;

        Assert.NotNull(target);
        Assert.Equal(RunTargetKind.ScriptFile, target.Kind);
        Assert.Equal(RunTargetDialect.Script, target.Dialect);
    }

    [Fact]
    public void The_llm_response_format_example_directory_resolves_its_crew_script()
    {
        var directory = ExamplePath("09-experimental", "llm-response-format");
        Assert.True(Directory.Exists(directory), $"Fixture moved: {directory}");

        var target = _detector.Detect(directory).Target;

        Assert.NotNull(target);
        Assert.Equal(RunTargetKind.ScriptDirectory, target.Kind);
        Assert.Equal(Path.Combine(directory, RunTargetDetector.CrewScriptFileName), target.RunPath);
    }

    [Fact]
    public void The_scripting_examples_directory_offers_its_scripts_as_candidates()
    {
        var directory = ExamplePath("scripting");
        Assert.True(Directory.Exists(directory), $"Fixture moved: {directory}");

        var detection = _detector.Detect(directory);

        Assert.Equal(RunTargetDetectionStatus.NeedsSelection, detection.Status);
        Assert.Contains(
            Path.Combine(directory, "01-hello-world.ork.ts"),
            detection.Candidates);
        Assert.All(detection.Candidates, candidate =>
            Assert.EndsWith(RunTargetDetector.ScriptSuffix, candidate, StringComparison.Ordinal));
    }

    [Fact]
    public void A_real_multi_file_tree_is_detected_and_carries_the_minimum_version_notice()
    {
        var directory = CreateTree("agents", "tasks");
        File.WriteAllText(Path.Combine(directory, "config.yaml"), "name: fixture\n");
        File.WriteAllText(Path.Combine(directory, "agents", "researcher.yaml"), "role: Researcher\n");

        var target = _detector.Detect(directory).Target;

        Assert.NotNull(target);
        Assert.Equal(RunTargetKind.MultiFileCrewDirectory, target.Kind);
        Assert.Equal(directory, target.RunPath);
        Assert.True(target.RequiresDirectoryRunSupport);

        // Advice, not a warning: directory dispatch is released, so nothing here blocks a run.
        var notice = Assert.Single(RunArgumentsBuilder.Validate(target));
        Assert.Equal(LaunchCodes.DirectoryRunNotice, notice.Code);
        Assert.Equal(ValidationSeverity.Information, notice.Severity);
        Assert.Contains(RunTargetRequirements.MinimumCliVersion, notice.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_real_flat_legacy_triplet_is_detected_the_way_the_cli_reads_it()
    {
        var directory = CreateTree();
        foreach (var name in RunTargetDetector.FlatLayoutFileNames)
            File.WriteAllText(Path.Combine(directory, name), "# fixture\n");

        var target = _detector.Detect(directory).Target;

        Assert.NotNull(target);
        Assert.Equal(RunTargetKind.MultiFileCrewDirectory, target.Kind);
        Assert.Equal(directory, target.RunPath);
    }

    [Fact]
    public void A_real_tree_holding_both_shapes_fails_naming_both()
    {
        var directory = CreateTree("agents");
        File.WriteAllText(Path.Combine(directory, RunTargetDetector.CrewScriptFileName), "export const crew = {};\n");

        var detection = _detector.Detect(directory);

        Assert.Equal(RunTargetCodes.AmbiguousDirectory, detection.ErrorCode);
        Assert.Contains(Path.Combine(directory, "agents"), detection.Candidates);
        Assert.Contains(Path.Combine(directory, RunTargetDetector.CrewScriptFileName), detection.Candidates);
    }

    [Fact]
    public void A_real_empty_directory_is_an_explicit_error()
    {
        var detection = _detector.Detect(CreateTree());

        Assert.Equal(RunTargetCodes.NoCandidate, detection.ErrorCode);
    }
}
