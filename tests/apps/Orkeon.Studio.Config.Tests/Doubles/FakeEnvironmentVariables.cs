using Orkeon.Studio.Core.Llm;

namespace Orkeon.Studio.Config.Tests.Doubles;

/// <summary>
/// An <see cref="IEnvironmentVariables"/> over two dictionaries — the process block and the user
/// scope — so a screen's probe reads the environment a test stages, never the machine's. Read only:
/// the TUI remembers no key. Names compare as on Linux and macOS, case included: two spellings are
/// two variables.
/// </summary>
public sealed class FakeEnvironmentVariables : IEnvironmentVariables
{
    /// <summary>The process environment.</summary>
    public Dictionary<string, string> Process { get; } = new(StringComparer.Ordinal);

    /// <summary>The user scope (<c>HKCU\Environment</c> on Windows).</summary>
    public Dictionary<string, string> User { get; } = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public string? Read(string name, EnvironmentVariableTarget target) => target switch
    {
        EnvironmentVariableTarget.Process => Process.GetValueOrDefault(name),
        EnvironmentVariableTarget.User => User.GetValueOrDefault(name),
        _ => throw new NotSupportedException($"The machine scope is never read ({target})."),
    };

    /// <inheritdoc />
    public IReadOnlyList<KeyValuePair<string, string>> ReadAll(EnvironmentVariableTarget target) => target switch
    {
        EnvironmentVariableTarget.Process => [.. Process],
        EnvironmentVariableTarget.User => [.. User],
        _ => throw new NotSupportedException($"The machine scope is never read ({target})."),
    };

    /// <inheritdoc />
    public void Write(string name, string? value, EnvironmentVariableTarget target) =>
        throw new InvalidOperationException("The TUI never writes an environment variable.");
}
