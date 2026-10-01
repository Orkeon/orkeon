using Orkeon.Studio.Core.Llm;

namespace Orkeon.Studio.Wpf.Tests.Doubles;

/// <summary>
/// An <see cref="IEnvironmentVariables"/> over two dictionaries — the process block and the
/// user scope — so a test can stage a Studio started from a parent older than the key.
/// </summary>
public sealed class FakeEnvironmentVariables : IEnvironmentVariables
{
    /// <summary>The process environment.</summary>
    public Dictionary<string, string> Process { get; } = new(StringComparer.Ordinal);

    /// <summary>The user scope (<c>HKCU\Environment</c> on Windows).</summary>
    public Dictionary<string, string> User { get; } = new(StringComparer.Ordinal);

    /// <summary>When set, every write to the user scope throws it.</summary>
    public Exception? UserWriteFailure { get; set; }

    /// <summary>The scopes written, in order.</summary>
    public List<EnvironmentVariableTarget> Writes { get; } = [];

    /// <inheritdoc />
    public string? Read(string name, EnvironmentVariableTarget target) =>
        ScopeOf(target).GetValueOrDefault(name);

    /// <inheritdoc />
    public void Write(string name, string? value, EnvironmentVariableTarget target)
    {
        if (target == EnvironmentVariableTarget.User && UserWriteFailure is { } failure)
            throw failure;

        Writes.Add(target);
        var scope = ScopeOf(target);
        if (value is null)
            scope.Remove(name);
        else
            scope[name] = value;
    }

    private Dictionary<string, string> ScopeOf(EnvironmentVariableTarget target) => target switch
    {
        EnvironmentVariableTarget.Process => Process,
        EnvironmentVariableTarget.User => User,
        _ => throw new NotSupportedException($"The machine scope is never touched ({target})."),
    };
}
