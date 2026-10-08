using Orkeon.Tests.Shared.Produced;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// A settings file of the repository writes only what Orkeon reads: every key is one of the settings
/// catalogue — the names an operator chooses aside —, every value one its key can take, and no key
/// is a retired one. The start validation protects a file a host reads; nothing protected the files
/// nobody starts: the templates of the examples, the sample of an installation, and the settings
/// Orkeon Studio's screenshots are taken on, which wrote two keys of <c>RateLimiting</c> that never
/// existed. Judged against the complete catalogue: a file may be the daemon's.
/// </summary>
public sealed class RepositorySettingsFilesTests
{
    [Fact]
    public void A_settings_file_of_the_repository_writes_only_keys_of_the_catalogue()
    {
        var problems = new List<string>();
        foreach (var file in RepositorySettingsFiles.All())
        {
            var text = ProducedFile.Read(file) ?? throw new InvalidOperationException($"{file} is listed and cannot be read.");
            using var settings = SettingsFileGuard.Read(text);
            if (settings is null)
            {
                problems.Add($"{file}: the file is not a JSON object.");
                continue;
            }

            problems.AddRange(SettingsFileGuard.Complete.Problems(settings.RootElement, openRoot: false).Select(problem => $"{file}: {problem}"));
        }

        Assert.True(
            problems.Count == 0,
            "A settings file of the repository writes what no host reads (`orkeon settings <section>` lists the keys of a section):\n"
            + string.Join("\n", problems));
    }

    [Fact]
    public void The_settings_files_of_the_repository_are_found()
    {
        var files = RepositorySettingsFiles.All();

        // A listing that stops matching would turn the control above into a silent pass.
        Assert.Contains("examples/appsettings/appsettings.json", files);
        Assert.Contains("examples/appsettings/appsettings.openai.local.json.example", files);
        Assert.Contains("examples/service-host/appsettings.host.json", files);
        Assert.Contains("scripts/installer-assets/appsettings.sample.json", files);
        Assert.Contains("scripts/smoke-onboarding/fixtures/rag-settings.json", files);
        Assert.Contains($"{RepositorySettingsFiles.StudioCaptureDirectory}/seeded.appsettings.json", files);
        Assert.Contains($"{RepositorySettingsFiles.StudioCaptureDirectory}/pristine.appsettings.json", files);
        Assert.True(files.Count >= 21, $"{files.Count} files: {string.Join(", ", files)}");
    }
}
