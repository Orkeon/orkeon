using System.Text.Json;
using Orkeon.Scripting.Cli.Commands.UseCases;
using Orkeon.Scripting.Cli.Tests.Doubles;

namespace Orkeon.Scripting.Cli.Tests.UseCases;

/// <summary>
/// The catalogue the <c>orkeon</c> tool carries (STUDIO-38 D-03): the manifest, each example's
/// crew file and its <c>data/</c> folder, embedded — the examples themselves are not shipped.
/// These tests read the repository next to the binary to prove the embedding is exact.
/// </summary>
public sealed class UseCaseCatalogTests
{
    private static readonly UseCaseCatalog Embedded = UseCaseCatalog.Embedded;

    [Fact]
    public void The_embedded_catalog_is_the_repository_manifest()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(RepoPath("examples", "usecases.json")));
        var ids = manifest.RootElement.GetProperty("useCases").EnumerateArray()
            .Select(entry => entry.GetProperty("id").GetString())
            .ToList();

        Assert.Equal(ids, Embedded.UseCases.Select(useCase => useCase.Id));
        Assert.Equal(UseCaseLanguages.All, Embedded.Languages);
    }

    [Fact]
    public void Every_use_case_carries_its_crew_file_byte_for_byte()
    {
        foreach (var useCase in Embedded.UseCases)
        {
            var onDisk = File.ReadAllText(RepoPath("examples", useCase.Category, useCase.Id, useCase.CrewFileName));

            Assert.Contains(Embedded.FilesOf(useCase.Id), file => file.Path == useCase.CrewFileName);
            Assert.Equal(onDisk, Embedded.ReadCrew(useCase.Id));
        }
    }

    [Fact]
    public void Sample_data_is_embedded_exactly_for_the_examples_that_have_it()
    {
        foreach (var useCase in Embedded.UseCases)
        {
            var embedded = Embedded.FilesOf(useCase.Id)
                .Where(file => file.Path.StartsWith("data/", StringComparison.Ordinal))
                .Select(file => file.Path)
                .Order(StringComparer.Ordinal)
                .ToList();

            var dataDirectory = RepoPath("examples", useCase.Category, useCase.Id, "data");
            var onDisk = Directory.Exists(dataDirectory)
                ? Directory.EnumerateFiles(dataDirectory, "*", SearchOption.AllDirectories)
                    .Select(path => "data/" + Path.GetRelativePath(dataDirectory, path).Replace('\\', '/'))
                    .Order(StringComparer.Ordinal)
                    .ToList()
                : [];

            Assert.Equal(useCase.HasSampleData, embedded.Count > 0);
            Assert.Equal(onDisk, embedded);
        }
    }

    [Fact]
    public void A_data_file_reads_back_byte_for_byte()
    {
        var useCase = Embedded.Get("01-research-assistant");
        var file = Embedded.FilesOf(useCase.Id).First(f => f.Path == "data/ev-sales-by-region.csv");
        var onDisk = File.ReadAllBytes(RepoPath("examples", useCase.Category, useCase.Id, "data", "ev-sales-by-region.csv"));

        using var stream = Embedded.OpenFile(useCase.Id, file.Path);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);

        Assert.Equal(onDisk, copy.ToArray());
        Assert.Equal(onDisk.LongLength, file.Length);
    }

    /// <summary>DC-3: the finance examples are searchable references; their shared tools stay out.</summary>
    [Fact]
    public void The_finance_shared_tools_are_not_embedded()
    {
        var finance = Embedded.UseCases.Where(u => u.Category == "03-finance-trading").ToList();

        Assert.Equal(15, finance.Count);
        Assert.All(finance, useCase => Assert.False(useCase.Importable));
        Assert.All(finance, useCase => Assert.Equal("main.ork.ts", useCase.CrewFileName));
        Assert.DoesNotContain(EmbeddedUseCaseFileSource.Instance.Paths, path => path.Contains("_tools", StringComparison.Ordinal));
    }

    [Fact]
    public void Only_catalogued_examples_are_embedded()
    {
        var catalogued = Embedded.UseCases.Select(u => $"{u.Category}/{u.Id}/").ToList();

        Assert.All(
            EmbeddedUseCaseFileSource.Instance.Paths.Where(path => path != UseCaseCatalog.ManifestPath),
            path => Assert.Contains(catalogued, prefix => path.StartsWith(prefix, StringComparison.Ordinal)));
    }

    [Fact]
    public void An_unknown_id_is_a_typed_error()
    {
        var error = Assert.Throws<UnknownUseCaseException>(() => Embedded.Get("99-no-such-example"));

        Assert.Equal("99-no-such-example", error.UseCaseId);
        Assert.Equal(UseCaseErrorCodes.UnknownId, UnknownUseCaseException.Code);
        Assert.False(Embedded.TryGet("99-no-such-example", out _));
    }

    [Fact]
    public void A_title_falls_back_to_another_language_when_the_asked_one_is_empty()
    {
        var catalog = UseCaseCatalog.Parse(
            UseCaseFixtures.Manifest.Replace("\"en\": \"Daily email digest\"", "\"en\": \"\"", StringComparison.Ordinal),
            new FakeUseCaseFileSource());

        var digest = catalog.Get(UseCaseFixtures.MailDigest);

        Assert.Equal("Résumé quotidien des e-mails", digest.TitleIn("en"));
        Assert.Equal("每日邮件摘要", digest.TitleIn("zh-Hans"));
        Assert.Equal(string.Empty, catalog.Get(UseCaseFixtures.Untitled).TitleIn("fr"));
    }

    [Fact]
    public void A_crew_file_is_read_from_the_source_by_category_and_id()
    {
        var files = UseCaseFixtures.Files();
        var catalog = UseCaseCatalog.Parse(UseCaseFixtures.Manifest, files);

        var crew = catalog.ReadCrew(UseCaseFixtures.MailDigest);

        Assert.Contains("daily-mail-digest", crew, StringComparison.Ordinal);
        Assert.Equal(["01-enterprise/01-daily-mail-digest/config.yaml"], files.Opened);
        Assert.Equal(["config.yaml", "data/inbox.eml"], catalog.FilesOf(UseCaseFixtures.MailDigest).Select(f => f.Path));
    }

    private static string RepoPath(params string[] parts)
    {
        // Climb from bin/<config>/<tfm>/ until the solution file shows up rather than
        // counting "..": the test output layout has moved before.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Orkeon.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, $"Could not locate the repo root above {AppContext.BaseDirectory}.");
        return Path.Combine([dir!.FullName, .. parts]);
    }
}
