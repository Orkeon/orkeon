using Orkeon.Studio.Core.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// STUDIO-31, D-02: the adoption writes <c>studio-team.json</c> by merge. It used to build a new
/// record every time, and a « Modify » erased whatever it did not own.
/// </summary>
public partial class CreateTeamWizardTests
{
    /// <summary>
    /// A re-adoption writes the fields it owns — the name, the need, the profile, the schedule, the
    /// folders — and keeps the others: the archive flag and its date, the last run.
    /// </summary>
    [Fact]
    public async Task A_readoption_keeps_the_archive_flag_and_the_last_run()
    {
        var root = Path.Combine(Path.GetTempPath(), "orkeon-wiz-merge-" + Guid.NewGuid().ToString("N"));
        var sessionDir = Path.Combine(root, ".orkeon", "forge", "veille");
        var teamDir = Path.Combine(root, "teams", "veille-docs");
        var archivedAt = new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);
        var lastRunAt = new DateTimeOffset(2026, 9, 18, 7, 30, 0, TimeSpan.Zero);
        Directory.CreateDirectory(sessionDir);
        Directory.CreateDirectory(teamDir);
        await File.WriteAllTextAsync(Path.Combine(sessionDir, "session.json"),
            $$"""{"v":1,"id":"{{ReopenedSessionId}}","slug":"veille","title":"Veille","format":"yaml","state":"Promoted","status":"Promoted","promotedTo":{{System.Text.Json.JsonSerializer.Serialize(teamDir)}}}""", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(sessionDir, "blueprint.json"),
            """{"crew":{"name":"veille"},"agents":[{"key":"a","role":"A","tools":["file_read"]}],"tasks":[{"key":"t","description":"d","agent":"a"}]}""", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(teamDir, "studio-team.json"),
            """{"name":"Veille docs","description":"le besoin d'origine","profile":"Local","archived":true,"archivedAt":"2026-09-20T08:00:00+00:00","lastRunAt":"2026-09-18T07:30:00+00:00"}""", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(teamDir, "forge.json"),
            $$"""{"v":1,"id":"{{ReopenedSessionId}}","slug":"veille"}""", TestContext.Current.CancellationToken);
        try
        {
            var (vm, processes, _) = Build(teamsRoot: Path.Combine(root, "teams"));
            processes.NextRuns.Enqueue(FoundStream(sessionDir, teamDir));
            processes.OutputToEmit.AddRange(
            [
                Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"SESSION","format":"yaml","resumed":true}""".Replace("SESSION", System.Text.Json.JsonSerializer.Serialize(sessionDir).Trim('"'), StringComparison.Ordinal)),
                Out("""{"v":2,"seq":2,"ts":"t","kind":"stage.entered","stage":"ready","iteration":1}"""),
                Out("""{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""),
            ]);
            await vm.ReopenTeamAsync(TeamCatalog.Describe(teamDir));

            vm.TeamName = "Veille renommée";
            processes.OutputToEmit.Clear();
            processes.OutputToEmit.Add(Out(
                """{"v":2,"seq":1,"ts":"t","kind":"promoted","path":PATH,"launcher":"run.sh","updated":true}"""
                    .Replace("PATH", System.Text.Json.JsonSerializer.Serialize(teamDir), StringComparison.Ordinal)));
            await vm.SaveTeamCommand.ExecuteAsync();

            var readopted = TeamCatalog.Describe(teamDir);
            Assert.Equal("Veille renommée", readopted.Name);
            Assert.Equal("le besoin d'origine", readopted.Description);
            Assert.True(readopted.IsArchived);
            Assert.Equal(archivedAt, readopted.ArchivedAt);
            Assert.Equal(lastRunAt, readopted.LastRunAt);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
