using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Wpf.ViewModels.Services;

namespace Orkeon.Studio.Wpf.Tests.Doubles;

/// <summary>An in-memory <see cref="IAppSettingsStore"/>: a path-to-JSON map, no disk involved.</summary>
public sealed class FakeAppSettingsStore : IAppSettingsStore
{
    public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

    public List<string> SavedPaths { get; } = [];

    public string? LastSavedJson { get; private set; }

    public string? LoadError { get; set; }

    public bool Exists(string path) => Files.ContainsKey(path);

    public Task<(AppSettingsDocument? Document, string? ErrorMessage)> TryLoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (LoadError is { Length: > 0 })
            return Task.FromResult<(AppSettingsDocument?, string?)>((null, LoadError));

        if (!Files.TryGetValue(path, out var json))
            return Task.FromResult<(AppSettingsDocument?, string?)>((null, $"File not found: {path}"));

        return Task.FromResult(AppSettingsDocument.TryParse(json, out var document, out var error)
            ? ((AppSettingsDocument?)document, (string?)null)
            : (null, error));
    }

    public Task SaveAsync(AppSettingsDocument document, string path, CancellationToken cancellationToken = default)
    {
        LastSavedJson = document.ToJson();
        Files[path] = LastSavedJson;
        SavedPaths.Add(path);
        return Task.CompletedTask;
    }
}
