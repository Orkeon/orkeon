using Orkeon.ConsoleApp.Services;

namespace Orkeon.ConsoleApp.Tests.Fakes;

/// <summary>Counts runs and returns a scripted output, or throws a scripted exception.</summary>
internal sealed class FakeStartScriptRunner : IStartScriptRunner
{
    public int CallCount;
    public string? LastPath;
    public StartScriptOutput Output = new(string.Empty, string.Empty);
    public Exception? ToThrow;

    public Task<StartScriptOutput> RunAsync(string scriptPath, CancellationToken ct)
    {
        CallCount++;
        LastPath = scriptPath;
        if (ToThrow is not null)
            throw ToThrow;
        return Task.FromResult(Output);
    }
}
