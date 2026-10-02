using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.FileSystem;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Rag.Ingestion;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// GAP-27, through the runner host: the two <c>rag:</c> examples of
/// <c>docs/architecture/yaml-schema.md</c> ingest what they show. They wrote crew-relative globs
/// (<c>./data/catalogue/**/*.pdf</c>) and a directory (<c>./docs/procedures/</c>), and both failed
/// at ingestion — the crew's <c>sources</c> reached the pipeline verbatim, where no loader takes a
/// pattern or a folder. Each example is read from the page itself, completed into a crew (a name,
/// a goal, one task), laid out with the files it names, and loaded the way <c>orkeon run
/// crew.yaml</c> loads it — the crew's folder mounted at <c>/crew</c>; the ingestion manifest then
/// lists every file.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed partial class RunnerHostRagSourcesTests : IDisposable
{
    private readonly string _root;
    private readonly string _crewDirectory;
    private readonly string _output;

    public RunnerHostRagSourcesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "orkeon-hosting-rag-sources-" + Guid.NewGuid().ToString("N"));
        _crewDirectory = Directory.CreateDirectory(Path.Combine(_root, "crew")).FullName;
        _output = Directory.CreateDirectory(Path.Combine(_root, "out")).FullName;
        File.WriteAllText(Path.Combine(_root, "appsettings.json"), "{ \"RaggableTree\": { \"Enabled\": false } }");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort temp cleanup */ }
    }

    [Fact]
    public async Task The_short_form_example_ingests_the_catalogue_glob_and_the_faq()
    {
        var invoices = Path.Combine(RepositoryRoot(), "examples", "03-finance-trading", "40-invoice-processing", "data", "invoices");
        var ct = TestContext.Current.CancellationToken;
        Place("data/catalogue/INV-2024-0001.pdf", await File.ReadAllBytesAsync(Path.Combine(invoices, "INV-2024-0001.pdf"), ct));
        Place("data/catalogue/2024/INV-2024-0002.pdf", await File.ReadAllBytesAsync(Path.Combine(invoices, "INV-2024-0002.pdf"), ct));
        Place("data/faq.md", "Refund policy: customers may request a full refund within 30 days of purchase."u8.ToArray());
        Place("data/notes.txt", "Not under the catalogue, not named: not ingested."u8.ToArray());

        var ingested = await LoadAndListIngestedAsync(DocumentedExample("Short form"), "produits");

        Assert.Equal(
            ["/crew/data/catalogue/2024/INV-2024-0002.pdf", "/crew/data/catalogue/INV-2024-0001.pdf", "/crew/data/faq.md"],
            ingested);
    }

    [Fact]
    public async Task The_long_form_example_ingests_every_file_of_its_directory()
    {
        Place("docs/procedures/onboarding.md", "Onboarding: a new customer gets an account within a day."u8.ToArray());
        Place("docs/procedures/refunds/policy.md", "Refunds are issued within 30 days."u8.ToArray());
        Place("docs/elsewhere.md", "Outside the procedures folder."u8.ToArray());

        var ingested = await LoadAndListIngestedAsync(DocumentedExample("Long form"), "procedures");

        Assert.Equal(["/crew/docs/procedures/onboarding.md", "/crew/docs/procedures/refunds/policy.md"], ingested);
    }

    /// <summary>
    /// Writes <paramref name="snippet"/>, completed into a crew, as <c>crew.yaml</c>; loads it the
    /// way the runner does; returns the sources the manifest of <paramref name="collection"/> lists.
    /// </summary>
    private async Task<IReadOnlyList<string>> LoadAndListIngestedAsync(string snippet, string collection)
    {
        var agent = FirstAgentKey().Match(snippet);
        Assert.True(agent.Success, "The documented example declares no agent:\n" + snippet);
        var crew = "name: \"documented-example\"\ngoal: \"Answer from the documented knowledge\"\n" + snippet.TrimEnd() +
            $"\n\ntasks:\n  answer:\n    description: \"Answer the question.\"\n    expectedOutput: \"An answer.\"\n    agent: \"{agent.Groups["key"].Value}\"\n";
        await File.WriteAllTextAsync(Path.Combine(_crewDirectory, "crew.yaml"), crew, TestContext.Current.CancellationToken);

        var original = Console.Error;
        using var muted = new StringWriter();
        Console.SetError(muted);
        try
        {
            using var host = RunnerHost.Build(
                Path.Combine(_root, "appsettings.json"),
                new RunnerMountPlan
                {
                    CliMounts =
                    [
                        $"{FileSystemMount.Quote(_crewDirectory)}:/crew:ro",
                        $"{FileSystemMount.Quote(_output)}:/output:rw",
                    ],
                    AllowExternalMounts = true,
                },
                configureLogging: (_, logging) => logging.SetMinimumLevel(LogLevel.None),
                configureServices: (_, services) => services.AddSingleton<IEmbeddingProvider>(new LexicalEmbeddingProvider()));

            var scope = host.Services.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var factory = scope.ServiceProvider.GetRequiredService<ICrewFactory>();
                await RunnerExecution.LoadCrewAsync(
                    host, factory, "/crew/crew.yaml", NullLogger.Instance, TestContext.Current.CancellationToken);
            }

            var manifest = await host.Services.GetRequiredService<IIngestionManifestStore>()
                .LoadAsync(collection, TestContext.Current.CancellationToken);
            Assert.True(manifest is not null, $"Nothing was ingested into '{collection}'.\n{muted}");
            return [.. manifest.Sources.Keys.Order(StringComparer.Ordinal)];
        }
        finally
        {
            Console.SetError(original);
        }
    }

    private void Place(string relativePath, byte[] content)
    {
        var path = Path.Combine(_crewDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    /// <summary>The first <c>```yaml</c> block after the line of <c>yaml-schema.md</c> that starts with <paramref name="lead"/>.</summary>
    private static string DocumentedExample(string lead)
    {
        var page = File.ReadAllLines(Path.Combine(RepositoryRoot(), "docs", "architecture", "yaml-schema.md"));
        var start = Array.FindIndex(page, line => line.StartsWith(lead, StringComparison.Ordinal));
        Assert.True(start >= 0, $"yaml-schema.md has no line starting with '{lead}'.");
        var open = Array.FindIndex(page, start, line => line.Trim() == "```yaml");
        var close = Array.FindIndex(page, open + 1, line => line.Trim() == "```");
        Assert.True(open > start && close > open, $"No YAML block follows '{lead}' in yaml-schema.md.");
        return string.Join('\n', page[(open + 1)..close]);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Orkeon.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Orkeon.sln not found above the test binaries.");
    }

    [GeneratedRegex(@"^agents:\s*\n\s+(?<key>[A-Za-z_][\w-]*):", RegexOptions.Multiline)]
    private static partial Regex FirstAgentKey();
}
