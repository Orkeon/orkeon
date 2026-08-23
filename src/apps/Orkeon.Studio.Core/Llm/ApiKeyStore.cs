namespace Orkeon.Studio.Core.Llm;

/// <summary>
/// Where a pasted API key goes: a named environment variable, never a file. The port exists
/// so the editor's behaviour is testable without touching the process environment.
/// </summary>
public interface IApiKeyStore
{
    /// <summary>The value currently held by <paramref name="envName"/>, or null.</summary>
    string? Peek(string envName);

    /// <summary>Stores <paramref name="value"/> under <paramref name="envName"/>.</summary>
    void Save(string envName, string value);
}

/// <summary>
/// The real store: the user-level environment (persistent across sessions on Windows;
/// user-level writes are a documented no-op elsewhere, where the WPF app does not run)
/// plus the process environment, so the current Studio session — and every child process
/// it spawns — sees the key immediately.
/// </summary>
public sealed class EnvironmentApiKeyStore : IApiKeyStore
{
    /// <inheritdoc />
    public string? Peek(string envName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(envName);
        var value = Environment.GetEnvironmentVariable(envName);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <inheritdoc />
    public void Save(string envName, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(envName);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var trimmed = value.Trim();
        Environment.SetEnvironmentVariable(envName, trimmed, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable(envName, trimmed, EnvironmentVariableTarget.Process);
    }
}
