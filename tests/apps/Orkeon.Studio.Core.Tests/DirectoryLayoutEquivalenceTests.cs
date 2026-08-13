using Orkeon.Hosting;
using Orkeon.Studio.Core.Targets;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// Studio decides what to point <c>orkeon run</c> at; <c>CrewDirectoryLayout.Inspect</c> is
/// what the CLI then does with a directory. When the two disagree, Studio either builds a
/// command line the CLI refuses, or refuses a directory the CLI would happily have run — both
/// of which a user experiences as Studio being broken. This matrix runs both classifiers over
/// real trees on disk and requires the same verdict for each.
/// </summary>
public sealed class DirectoryLayoutEquivalenceTests : IDisposable
{
    /// <summary>What both classifiers must agree on for a directory.</summary>
    public enum DirectoryVerdict
    {
        /// <summary>The directory itself can be handed to <c>orkeon run</c>.</summary>
        RunnableYamlDirectory,

        /// <summary>A YAML layout side by side with a scripting entry point: refused outright.</summary>
        Ambiguous,

        /// <summary>
        /// No YAML layout. Studio may still find a script to run inside it, but the directory
        /// is not itself a crew target.
        /// </summary>
        NotAYamlCrewDirectory,
    }

    /// <summary>Prefix <c>CrewDirectoryLayout</c> gives its ambiguity diagnostic.</summary>
    private const string AmbiguityMarker = "Ambiguous crew directory";

    private readonly RunTargetDetector _detector = new(PhysicalTargetProbe.Instance);

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "orkeon-studio-layout-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    /// <summary>Every fixture tree and the verdict both classifiers owe it.</summary>
    private static readonly (string Name, DirectoryVerdict Expected)[] Fixtures =
    [
        ("nominal-agents", DirectoryVerdict.RunnableYamlDirectory),
        ("nominal-tasks", DirectoryVerdict.RunnableYamlDirectory),
        ("nominal-both", DirectoryVerdict.RunnableYamlDirectory),
        ("flat", DirectoryVerdict.RunnableYamlDirectory),
        ("flat-plus-script", DirectoryVerdict.Ambiguous),
        ("agents-plus-crew-script", DirectoryVerdict.Ambiguous),
        ("agents-plus-other-script", DirectoryVerdict.Ambiguous),
        ("agents-plus-compiled-script", DirectoryVerdict.Ambiguous),
        ("scripts-only", DirectoryVerdict.NotAYamlCrewDirectory),
        ("crew-script-only", DirectoryVerdict.NotAYamlCrewDirectory),
        ("partial-flat", DirectoryVerdict.NotAYamlCrewDirectory),
        ("loose-yaml", DirectoryVerdict.NotAYamlCrewDirectory),
        ("empty", DirectoryVerdict.NotAYamlCrewDirectory),
    ];

    public static TheoryData<string, DirectoryVerdict> Matrix()
    {
        var data = new TheoryData<string, DirectoryVerdict>();
        foreach (var (name, expected) in Fixtures)
            data.Add(name, expected);

        return data;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Studio_and_the_cli_reach_the_same_verdict(string fixtureName, DirectoryVerdict expected)
    {
        var directory = Build(fixtureName);

        Assert.Equal(expected, CliVerdict(directory));
        Assert.Equal(expected, StudioVerdict(directory));
    }

    [Fact]
    public void The_ambiguity_marker_this_matrix_reads_still_exists()
    {
        // CliVerdict tells "ambiguous" from "no layout" by the diagnostic's opening words,
        // because CrewDirectoryInspection carries no code. Reword that message and this fails
        // loudly, instead of the matrix quietly grading every ambiguity as "no layout".
        var inspection = CrewDirectoryLayout.Inspect(Build("agents-plus-crew-script"));

        Assert.False(inspection.IsCrewDirectory);
        Assert.StartsWith(AmbiguityMarker, inspection.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Preferring_the_yaml_layout_never_produces_a_directory_the_cli_would_reject()
    {
        // The one preference a UI can offer that has no CLI equivalent: there is no flag that
        // makes `orkeon run <dir>` ignore a stray script.
        var directory = Build("agents-plus-other-script");

        var detection = _detector.Detect(directory, RunTargetKind.MultiFileCrewDirectory);

        Assert.False(detection.IsResolved);
        Assert.Equal(RunTargetCodes.YamlLayoutBlockedByScript, detection.ErrorCode);
        Assert.False(CrewDirectoryLayout.Inspect(directory).IsCrewDirectory);
    }

    [Fact]
    public void A_directory_studio_calls_runnable_is_one_the_cli_loads()
    {
        // The direction that matters most: every resolved MultiFileCrewDirectory must survive
        // Inspect, since that is the exact check `orkeon run` performs on the path Studio built.
        foreach (var (name, expected) in Fixtures)
        {
            if (expected != DirectoryVerdict.RunnableYamlDirectory)
                continue;

            var directory = Build(name);
            var target = _detector.Detect(directory).Target;

            Assert.NotNull(target);
            Assert.Equal(RunTargetKind.MultiFileCrewDirectory, target.Kind);
            Assert.True(
                CrewDirectoryLayout.Inspect(target.RunPath).IsCrewDirectory,
                $"Studio would run '{name}' as a directory but the CLI rejects it.");
        }
    }

    private static DirectoryVerdict CliVerdict(string directory)
    {
        var inspection = CrewDirectoryLayout.Inspect(directory);
        if (inspection.IsCrewDirectory)
            return DirectoryVerdict.RunnableYamlDirectory;

        return inspection.Error?.StartsWith(AmbiguityMarker, StringComparison.Ordinal) == true
            ? DirectoryVerdict.Ambiguous
            : DirectoryVerdict.NotAYamlCrewDirectory;
    }

    private DirectoryVerdict StudioVerdict(string directory)
    {
        var detection = _detector.Detect(directory);

        if (detection.IsResolved && detection.Target!.Kind == RunTargetKind.MultiFileCrewDirectory)
            return DirectoryVerdict.RunnableYamlDirectory;

        return detection.ErrorCode == RunTargetCodes.AmbiguousDirectory
            ? DirectoryVerdict.Ambiguous
            : DirectoryVerdict.NotAYamlCrewDirectory;
    }

    /// <summary>Materialises one fixture tree and returns its path.</summary>
    private string Build(string fixtureName)
    {
        var directory = Path.Combine(_root, fixtureName);
        if (Directory.Exists(directory))
            return directory;

        Directory.CreateDirectory(directory);

        switch (fixtureName)
        {
            case "nominal-agents":
                Folder(directory, "agents", "researcher.yaml");
                Write(directory, "config.yaml", "name: fixture\n");
                break;
            case "nominal-tasks":
                Folder(directory, "tasks", "research.yaml");
                Write(directory, "config.yaml", "name: fixture\n");
                break;
            case "nominal-both":
                Folder(directory, "agents", "researcher.yaml");
                Folder(directory, "tasks", "research.yaml");
                Write(directory, "crew.yaml", "name: fixture\n");
                Write(directory, "notes.md", "not a crew\n");
                break;
            case "flat":
                WriteFlatTriplet(directory);
                break;
            case "flat-plus-script":
                WriteFlatTriplet(directory);
                Write(directory, "crew.ork.ts", "export const crew = {};\n");
                break;
            case "agents-plus-crew-script":
                Folder(directory, "agents", "researcher.yaml");
                Write(directory, "crew.ork.ts", "export const crew = {};\n");
                break;
            case "agents-plus-other-script":
                Folder(directory, "agents", "researcher.yaml");
                Write(directory, "pipeline.ork.ts", "export const crew = {};\n");
                break;
            case "agents-plus-compiled-script":
                Folder(directory, "agents", "researcher.yaml");
                Write(directory, "bundle.ork.js", "export const crew = {};\n");
                break;
            case "scripts-only":
                Write(directory, "01-first.ork.ts", "export const crew = {};\n");
                Write(directory, "02-second.ork.ts", "export const crew = {};\n");
                break;
            case "crew-script-only":
                Write(directory, "crew.ork.ts", "export const crew = {};\n");
                break;
            case "partial-flat":
                Write(directory, "crew.yaml", "name: fixture\n");
                Write(directory, "agents.yaml", "- role: Researcher\n");
                break;
            case "loose-yaml":
                Write(directory, "anything.yaml", "name: fixture\n");
                break;
            case "empty":
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fixtureName), fixtureName, "Unknown fixture.");
        }

        return directory;
    }

    private static void WriteFlatTriplet(string directory)
    {
        foreach (var name in RunTargetDetector.FlatLayoutFileNames)
            Write(directory, name, "# fixture\n");
    }

    private static void Folder(string directory, string name, string childFileName)
    {
        var child = Path.Combine(directory, name);
        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(child, childFileName), "role: Researcher\n");
    }

    private static void Write(string directory, string name, string content) =>
        File.WriteAllText(Path.Combine(directory, name), content);
}
