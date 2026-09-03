namespace Orkeon.Host.Tests.Doubles;

/// <summary>Records every reported startup failure, in order.</summary>
internal sealed class RecordingFailureSink : IStartupFailureSink
{
    public List<string> Reported { get; } = [];

    public void Report(string message) => Reported.Add(message);
}
