namespace Orkeon.E2E.Tests;

/// <summary>
/// PUB-17 T1 (CLI leg): the real `orkeon` CLI, spawned as a process from the
/// source checkout, dry-runs bundled examples end-to-end (settings resolution,
/// host build, crew load under strict tool resolution) — no LLM call, no key.
/// Category=Slow: each --validate loads a full host, which takes minutes on slow
/// filesystems; the nightly integration workflow owns it.
/// </summary>
[Trait("Category", "Slow")]
[Collection(OrkeonCliFixture.CollectionName)]
public class CliValidateSlowTests
{
    private readonly OrkeonCliFixture _cli;

    /// <summary>Takes the shared CLI build; see <see cref="OrkeonCliFixture"/> for why.</summary>
    public CliValidateSlowTests(OrkeonCliFixture cli) => _cli = cli;

    private (int ExitCode, string Output) RunCli(string arguments, TimeSpan timeout) =>
        _cli.RunCli(arguments, _cli.RepoRoot, timeout);

    [Fact]
    public void Validate_SingleYamlExample_ExitsZero()
    {
        var (exitCode, output) = RunCli(
            "run examples/01-enterprise/01-research-assistant/config.yaml --validate",
            TimeSpan.FromMinutes(12));

        Assert.True(exitCode == 0, $"Expected exit 0, got {exitCode}. Output:\n{output}");
        Assert.Contains("VALIDATION OK", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_MultiFileCrewDirectory_ExitsZero()
    {
        var (exitCode, output) = RunCli(
            "run examples/crew-multifile --validate",
            TimeSpan.FromMinutes(12));

        Assert.True(exitCode == 0, $"Expected exit 0, got {exitCode}. Output:\n{output}");
        Assert.Contains("VALIDATION OK", output, StringComparison.OrdinalIgnoreCase);
    }
}
