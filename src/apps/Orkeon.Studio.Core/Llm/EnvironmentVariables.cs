namespace Orkeon.Studio.Core.Llm;

/// <summary>
/// The environment variables of one scope — the process block or the user's persistent scope.
/// A port so the key store's read and write order is testable without touching the machine.
/// </summary>
public interface IEnvironmentVariables
{
    /// <summary>The value of <paramref name="name"/> in <paramref name="target"/>, or null.</summary>
    string? Read(string name, EnvironmentVariableTarget target);

    /// <summary>
    /// Every variable of <paramref name="target"/>, as name/value pairs — the whole block, so a
    /// reader can find a key under any spelling the configuration accepts (STUDIO-56).
    /// </summary>
    IReadOnlyList<KeyValuePair<string, string>> ReadAll(EnvironmentVariableTarget target);

    /// <summary>Sets <paramref name="name"/> in <paramref name="target"/>; null removes it.</summary>
    void Write(string name, string? value, EnvironmentVariableTarget target);
}

/// <summary>
/// The machine's own environment, through <see cref="Environment"/>. Outside Windows the user
/// scope reads as absent and its writes are no-ops — the documented .NET behaviour.
/// </summary>
public sealed class SystemEnvironmentVariables : IEnvironmentVariables
{
    private SystemEnvironmentVariables()
    {
    }

    /// <summary>The single instance.</summary>
    public static SystemEnvironmentVariables Instance { get; } = new();

    /// <inheritdoc />
    public string? Read(string name, EnvironmentVariableTarget target) =>
        Environment.GetEnvironmentVariable(name, target);

    /// <inheritdoc />
    public IReadOnlyList<KeyValuePair<string, string>> ReadAll(EnvironmentVariableTarget target) =>
        [.. Environment.GetEnvironmentVariables(target)
            .Cast<System.Collections.DictionaryEntry>()
            .Select(entry => new KeyValuePair<string, string>((string)entry.Key, entry.Value as string ?? ""))];

    /// <inheritdoc />
    public void Write(string name, string? value, EnvironmentVariableTarget target) =>
        Environment.SetEnvironmentVariable(name, value, target);
}
