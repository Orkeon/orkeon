namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// xUnit collection that serialises every test driving the CLI in-process. A verb writes and
/// reads through the console it is handed (<see cref="TestConsole"/>), but these tests still share
/// process-global state: the current working directory a verb resolves its files against,
/// environment variables and static overrides (the global settings path, the init wizard's input),
/// and the process streams the hosts' console logger provider writes to, which
/// <see cref="ProcessLogCapture"/> swaps. They must not run concurrently.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CliCollection
{
    public const string Name = "cli-serial";
}
