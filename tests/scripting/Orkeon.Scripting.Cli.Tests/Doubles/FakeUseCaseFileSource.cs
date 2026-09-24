using System.Text;
using Orkeon.Scripting.Cli.Commands.UseCases;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IUseCaseFileSource"/>: the files a catalogue would embed, held in
/// memory under the same <c>&lt;category&gt;/&lt;id&gt;/&lt;path&gt;</c> names the CLI's resources use.
/// Records every path opened, so a test can prove a file was — or was not — read.
/// </summary>
internal sealed class FakeUseCaseFileSource : IUseCaseFileSource
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    /// <summary>Every path handed to <see cref="Open"/>, in order.</summary>
    public List<string> Opened { get; } = [];

    /// <inheritdoc />
    public IReadOnlyCollection<string> Paths => _files.Keys;

    /// <summary>Adds (or replaces) one text file, UTF-8 encoded.</summary>
    public FakeUseCaseFileSource With(string path, string text)
    {
        _files[path] = Encoding.UTF8.GetBytes(text);
        return this;
    }

    /// <inheritdoc />
    public Stream? Open(string path)
    {
        Opened.Add(path);
        return _files.TryGetValue(path, out var bytes) ? new MemoryStream(bytes, writable: false) : null;
    }
}
