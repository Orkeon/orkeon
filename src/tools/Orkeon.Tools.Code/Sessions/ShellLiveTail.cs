namespace Orkeon.Tools.Code.Sessions;

/// <summary>A job's in-memory output: its text, and whether bytes were dropped (the first line may be a cut fragment).</summary>
internal readonly record struct ShellLiveTail(string Text, bool Truncated);
