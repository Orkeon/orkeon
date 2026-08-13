using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Storage;

namespace Orkeon.Studio.Wpf.ViewModels.Services;

/// <summary>
/// Reads and writes the user's <c>appsettings.json</c>. The Core helper is a static class over the
/// physical disk; this seam lets the ViewModel tests run entirely in memory.
/// </summary>
public interface IAppSettingsStore
{
    /// <summary>Whether a file exists at <paramref name="path"/>.</summary>
    bool Exists(string path);

    /// <summary>Loads the document, or returns the reason it could not be loaded.</summary>
    Task<(AppSettingsDocument? Document, string? ErrorMessage)> TryLoadAsync(
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>Writes the document to <paramref name="path"/>, creating the directory if needed.</summary>
    Task SaveAsync(AppSettingsDocument document, string path, CancellationToken cancellationToken = default);
}

/// <summary>The production store: delegates straight to <see cref="AppSettingsFile"/>.</summary>
public sealed class PhysicalAppSettingsStore : IAppSettingsStore
{
    /// <summary>The shared instance; the type is stateless.</summary>
    public static PhysicalAppSettingsStore Instance { get; } = new();

    /// <inheritdoc />
    public bool Exists(string path) => AppSettingsFile.Exists(path);

    /// <inheritdoc />
    public Task<(AppSettingsDocument? Document, string? ErrorMessage)> TryLoadAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        AppSettingsFile.TryLoadAsync(path, cancellationToken);

    /// <inheritdoc />
    public Task SaveAsync(AppSettingsDocument document, string path, CancellationToken cancellationToken = default) =>
        AppSettingsFile.SaveAsync(document, path, cancellationToken);
}
