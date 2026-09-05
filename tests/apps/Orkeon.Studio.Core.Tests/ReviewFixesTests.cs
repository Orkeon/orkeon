using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Events;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// Pins of the RC2-FEAT-05 full-review fixes: the tolerant history write, the checklist
/// that hides no finding, the settings document that never destroys a scalar on removal,
/// and the mount collision severities aligned with what the runtime actually refuses.
/// </summary>
public sealed class ReviewFixesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"orkeon-review-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task A_history_write_that_the_disk_refuses_is_swallowed_not_thrown()
    {
        // The store's path IS a directory: WriteAllTextAsync must fail — and the store
        // must shrug (a finished run's result outweighs its history line).
        var asDirectory = Path.Combine(_root, "history.json");
        Directory.CreateDirectory(asDirectory);
        var store = new LaunchHistoryFileStore(asDirectory);

        var recorded = await store.RecordAsync(new LaunchHistoryEntry
        {
            Target = "crew.yaml",
            StartedAt = DateTimeOffset.UtcNow,
        }, TestContext.Current.CancellationToken);

        // The caller still gets the history it asked for...
        Assert.Equal("crew.yaml", Assert.Single(recorded.Entries).Target);

        // ...and the refused write left nothing behind: the next load reads an empty
        // history rather than a half-written one.
        var reloaded = await store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Empty(reloaded.Entries);
    }

    [Fact]
    public void A_finding_naming_an_unknown_criterion_still_shows_as_a_failed_line()
    {
        var model = new ForgeSessionModel();
        string[] stream =
        [
            """{"v":2,"seq":1,"ts":"t","kind":"brief.ready","brief":{"goal":"g","acceptance":[{"id":"A1","statement":"Cite ses sources","kind":"must"}]}}""",
            """{"v":2,"seq":2,"ts":"t","kind":"verdict.ready","score":0.2,"passing":false,"findings":[{"id":"F1","severity":"major","acceptance":"A9","statement":"Un constat orphelin"}],"suggestions":[],"judge":"llm"}""",
        ];
        foreach (var line in stream)
        {
            Assert.True(OrkeonEventParser.TryParse(line, out var parsed), line);
            model.Feed(parsed!);
        }

        var checklist = model.BuildChecklist();
        // The orphan (A9 matches nothing) is a failed line, not a silent drop.
        Assert.Contains(checklist, item => item.Statement == "Un constat orphelin" && item.Passed == false);
        Assert.Contains(checklist, item => item.Statement == "Cite ses sources");
    }

    [Fact]
    public void Removing_a_key_under_a_scalar_parent_leaves_the_scalar_untouched()
    {
        var document = AppSettingsDocument.Parse("""{"Orkeon":{"Llm":"oops"}}""");

        document.SetNode("Orkeon:Llm:Model", null);

        Assert.Contains("\"oops\"", document.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_exact_duplicate_virtual_path_blocks_a_near_collision_warns()
    {
        var validator = new MountValidator(new Doubles.FakeDirectoryProbe("/a", "/b"));

        var near = validator.Validate(["/a:/data:ro", "/b:/Data:rw"], requireAtLeastOne: false);
        var nearCollision = Assert.Single(near, m => m.Code == ValidationCodes.MountVirtualCollision);
        Assert.Equal(ValidationSeverity.Warning, nearCollision.Severity);

        var exact = validator.Validate(["/a:/data:ro", "/b:/data:rw"], requireAtLeastOne: false);
        var exactCollision = Assert.Single(exact, m => m.Code == ValidationCodes.MountVirtualCollision);
        Assert.Equal(ValidationSeverity.Error, exactCollision.Severity);
    }
}
