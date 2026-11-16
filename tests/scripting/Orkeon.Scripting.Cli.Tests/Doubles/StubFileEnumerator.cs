using System.Collections;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>A scripted file walk: each step yields a file or throws an <see cref="IOException"/> on MoveNext.</summary>
internal sealed class StubFileEnumerator : IEnumerator<FileInfo>
{
    private readonly Queue<FileInfo?> _steps;

    /// <summary>Each step is a file to yield, or null for a MoveNext that throws.</summary>
    public StubFileEnumerator(params FileInfo?[] steps) => _steps = new Queue<FileInfo?>(steps);

    public FileInfo Current { get; private set; } = null!;

    object IEnumerator.Current => Current;

    public bool MoveNext()
    {
        if (_steps.Count == 0)
            return false;
        var step = _steps.Dequeue();
        if (step is null)
            throw new IOException("scripted walk failure");
        Current = step;
        return true;
    }

    public void Reset() => throw new NotSupportedException();

    public void Dispose()
    {
    }
}
