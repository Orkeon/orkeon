using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Storage;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// Save targets and disk round-trip. Tests may use the real disk (CLAUDE.md VFS
/// exception for tests); everything happens under a per-test temporary directory.
/// </summary>
public sealed class SettingsStorageTests : IDisposable
{
    private static readonly int[] ChainOrders = [1, 2, 3, 4];

    private readonly string _tempDir;

    public SettingsStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-studio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void The_resolution_chain_has_the_four_documented_steps_in_order()
    {
        var chain = SettingsLocations.ResolutionChain;

        Assert.Equal(4, chain.Count);
        Assert.Equal(ChainOrders, chain.Select(s => s.Order));
        Assert.All(chain, step =>
        {
            Assert.False(string.IsNullOrWhiteSpace(step.Title));
            Assert.False(string.IsNullOrWhiteSpace(step.Description));
        });
        Assert.Contains("--settings", chain[0].Description, StringComparison.Ordinal);
        Assert.Contains("appsettings.json", chain[1].Description, StringComparison.Ordinal);
        Assert.Contains("orkeon init", chain[3].Description, StringComparison.Ordinal);
    }

    [Fact]
    public void The_global_path_is_absolute_and_named_appsettings_json()
    {
        if (!SettingsLocations.TryGetGlobalSettingsPath(out var path, out var error))
        {
            // A bare container without HOME has no per-user directory; the message must
            // then say what to do about it rather than surface as a crash.
            Assert.Contains("HOME", error, StringComparison.Ordinal);
            return;
        }

        Assert.NotNull(path);
        Assert.True(Path.IsPathRooted(path));
        Assert.Equal(AppSettingsDocument.FileName, Path.GetFileName(path));
        Assert.Equal("Orkeon", Path.GetFileName(Path.GetDirectoryName(path)));
    }

    [Fact]
    public void A_directory_target_receives_an_appsettings_json()
    {
        var normalized = SettingsLocations.NormalizeTargetPath(_tempDir);

        Assert.Equal(Path.Combine(_tempDir, AppSettingsDocument.FileName), normalized);
    }

    [Fact]
    public void A_file_target_is_taken_as_is_and_made_absolute()
    {
        var target = Path.Combine(_tempDir, "custom.json");

        Assert.Equal(target, SettingsLocations.NormalizeTargetPath(target));
        Assert.True(Path.IsPathRooted(SettingsLocations.NormalizeTargetPath("relative.json")));
    }

    [Fact]
    public async Task Save_creates_the_parent_directory_and_load_returns_the_same_document()
    {
        var target = Path.Combine(_tempDir, "nested", "dir", AppSettingsDocument.FileName);
        var created = LlmPresets.TryCreatePlan(LlmPresets.Ollama, null, out var plan, out _);
        Assert.True(created);
        Assert.NotNull(plan);
        var document = LlmPresets.BuildDocument(plan);

        await AppSettingsFile.SaveAsync(document, target, TestContext.Current.CancellationToken);

        Assert.True(AppSettingsFile.Exists(target));
        var reloaded = await AppSettingsFile.LoadAsync(target, TestContext.Current.CancellationToken);
        Assert.Equal(document.ToJson(), reloaded.ToJson());
        Assert.Equal(document.ToJson(), await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Saving_a_loaded_document_preserves_the_keys_studio_does_not_model()
    {
        var target = Path.Combine(_tempDir, AppSettingsDocument.FileName);
        const string original = """
            {
              "Llm": { "Model": "m", "BaseUrl": "http://localhost:11434" },
              "SomethingElse": { "kept": [1, 2, 3] }
            }
            """;
        await File.WriteAllTextAsync(target, original, TestContext.Current.CancellationToken);

        var document = await AppSettingsFile.LoadAsync(target, TestContext.Current.CancellationToken);
        document.Llm.Model = "llama3.2";
        await AppSettingsFile.SaveAsync(document, target, TestContext.Current.CancellationToken);

        var reloaded = await AppSettingsFile.LoadAsync(target, TestContext.Current.CancellationToken);
        Assert.Equal("llama3.2", reloaded.Llm.Model);
        Assert.Equal("[1,2,3]", reloaded.GetNode("SomethingElse:kept")!.ToJsonString());
    }

    [Fact]
    public async Task TryLoad_reports_a_missing_file()
    {
        var (document, error) = await AppSettingsFile.TryLoadAsync(Path.Combine(_tempDir, "absent.json"), TestContext.Current.CancellationToken);

        Assert.Null(document);
        Assert.Contains("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TryLoad_reports_a_malformed_file()
    {
        var target = Path.Combine(_tempDir, "broken.json");
        await File.WriteAllTextAsync(target, "{ nope", TestContext.Current.CancellationToken);

        var (document, error) = await AppSettingsFile.TryLoadAsync(target, TestContext.Current.CancellationToken);

        Assert.Null(document);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
