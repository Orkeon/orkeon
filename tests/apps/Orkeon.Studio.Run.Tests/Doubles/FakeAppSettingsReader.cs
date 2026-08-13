using Orkeon.Studio.Run.Launcher;

namespace Orkeon.Studio.Run.Tests.Doubles;

/// <summary>
/// In-memory <see cref="IAppSettingsReader"/>: the appsettings files are declared by the
/// test, so the launcher's mount preview never depends on the machine's disk.
/// </summary>
public sealed class FakeAppSettingsReader : IAppSettingsReader
{
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

    /// <summary>Paths passed to <see cref="TryRead"/>, in call order.</summary>
    public List<string> ReadPaths { get; } = new();

    /// <summary>Declares a file with its JSON content.</summary>
    public FakeAppSettingsReader WithFile(string path, string json)
    {
        _files[path] = json;
        return this;
    }

    /// <inheritdoc />
    public bool TryRead(string path, out string? json, out string? failureReason)
    {
        ReadPaths.Add(path);

        if (_files.TryGetValue(path, out var content))
        {
            json = content;
            failureReason = null;
            return true;
        }

        json = null;
        failureReason = $"'{path}' does not exist yet.";
        return false;
    }
}
