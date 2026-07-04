namespace Orkeon.Cli.Abstractions.Tests;

/// <summary>
/// Serializes tests that mutate process-wide ambient logger state.
/// </summary>
[CollectionDefinition("AmbientLogger", DisableParallelization = true)]
public sealed class AmbientLoggerCollection;

/// <summary>
/// Serializes tests that redirect the shared <see cref="System.Console"/> streams.
/// </summary>
[CollectionDefinition("SystemConsole", DisableParallelization = true)]
public sealed class SystemConsoleCollection;
