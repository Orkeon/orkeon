using System.Runtime.CompilerServices;
using Orkeon.Infrastructure.DependencyInjection;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// GAP-40, decision 5 — every settings file the repository hands an operator to copy passes the start
/// validation of <c>orkeon run</c>: the examples' <c>appsettings*.json</c>, the <c>*.json.example</c>
/// templates, and the fixtures of the release smokes. Nothing checked them: <c>Llm:Provider</c> and
/// <c>Llm:ApiKeyEnvironmentVariable</c>, which nothing reads, sat in two of them, and the smoke fixture
/// still wrote <c>Orkeon:Rag:ConnectionString</c> — its database stayed in memory, and the search
/// process could find nothing the ingestion process had written. What depends on the machine — the
/// mounts' folders, the variables holding the keys — is not judged here: the settings are.
/// </summary>
public sealed class ExampleSettingsTests
{
    /// <summary>Build outputs carry copies of the files; only the tracked sources are judged.</summary>
    private static readonly string[] s_outputs = ["bin", "obj", "obj-linux", "node_modules"];

    public static TheoryData<string> Files()
    {
        var root = RepositoryRoot();
        var examples = Path.Combine(root, "examples");
        var found = Directory.EnumerateFiles(examples, "*appsettings*.json", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(examples, "*.json.example", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "scripts", "smoke-onboarding", "fixtures"), "*.json"))
            .Where(path => !Path.GetRelativePath(root, path)
                .Split(Path.DirectorySeparatorChar)
                .Any(segment => s_outputs.Contains(segment, StringComparer.Ordinal)))
            .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal);

        var data = new TheoryData<string>();
        foreach (var file in found)
            data.Add(file);
        return data;
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void An_example_settings_file_passes_the_start_validation_of_orkeon_run(string file)
    {
        var refusals = RunnerHost.ValidateSettings(
            Path.Combine(RepositoryRoot(), file),
            (_, services) =>
            {
                services.AddOrkeonHumanInput();
                services.AddSemanticSearchTool();
            });

        Assert.Empty(refusals);
    }

    [Fact]
    public void The_example_settings_files_are_found()
    {
        // 17 example files and the smoke fixture, at the time of writing: a pattern that stops
        // matching would turn the theory above into a silent pass.
        Assert.True(Files().Count >= 18, $"{Files().Count} files");
    }

    private static string RepositoryRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
}
