namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// xUnit collection that serialises every test driving the CLI in-process. These tests
/// redirect the process-global <see cref="Console"/> streams and depend on the current
/// working directory, so they must not run concurrently.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CliCollection
{
    public const string Name = "cli-serial";
}
