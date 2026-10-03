namespace Orkeon.Host.Tests.Doubles;

/// <summary>Records every progress line reported to it, in order — synchronously, unlike <see cref="Progress{T}"/>.</summary>
internal sealed class RecordingProgress : IProgress<string>
{
    public List<string> Lines { get; } = [];

    public void Report(string value) => Lines.Add(value);
}
