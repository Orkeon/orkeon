namespace Orkeon.Studio.Core.Llm;

/// <summary>
/// Where a pasted API key goes: a named environment variable, never a file. The port exists
/// so the editor's behaviour is testable without touching the process environment.
/// </summary>
public interface IApiKeyStore
{
    /// <summary>The value currently held by <paramref name="envName"/>, or null.</summary>
    string? Peek(string envName);

    /// <summary>
    /// Stores <paramref name="value"/> under <paramref name="envName"/>. When the call returns,
    /// the key is in place for this session and every child it spawns; the task completes once
    /// it is also kept for the next sessions, and faults with the reason when that fails.
    /// </summary>
    Task SaveAsync(string envName, string value);
}

/// <summary>
/// The real store, over two scopes of the environment (STUDIO-44).
/// <list type="bullet">
/// <item>Read: the process block first, then the user scope (<c>HKCU\Environment</c> on Windows;
/// .NET reads it as absent elsewhere, where the WPF app does not run). A key found only in the
/// user scope — Studio started by a parent older than the key — is copied into the process, so
/// every child process Studio spawns inherits it.</item>
/// <item>Write: the process block first, synchronously, so the session sees the key at once;
/// then the user scope on a background thread, since that write broadcasts
/// <c>WM_SETTINGCHANGE</c> and may block. Its failure faults the returned task — the session
/// keeps its key either way.</item>
/// </list>
/// </summary>
public sealed class EnvironmentApiKeyStore : IApiKeyStore
{
    private readonly IEnvironmentVariables _environment;

    /// <summary>Builds the store over <paramref name="environment"/>, the machine's own when null.</summary>
    public EnvironmentApiKeyStore(IEnvironmentVariables? environment = null)
    {
        _environment = environment ?? SystemEnvironmentVariables.Instance;
    }

    /// <inheritdoc />
    public string? Peek(string envName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(envName);

        if (Clean(_environment.Read(envName, EnvironmentVariableTarget.Process)) is { } inProcess)
            return inProcess;

        if (Clean(ReadUserScope(envName)) is not { } inUserScope)
            return null;

        _environment.Write(envName, inUserScope, EnvironmentVariableTarget.Process);
        return inUserScope;
    }

    /// <inheritdoc />
    public Task SaveAsync(string envName, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(envName);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var trimmed = value.Trim();
        _environment.Write(envName, trimmed, EnvironmentVariableTarget.Process);
        return Task.Run(() => _environment.Write(envName, trimmed, EnvironmentVariableTarget.User));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031",
        Justification = "A user scope that cannot be read (registry access denied) is a key not found, " +
                        "exactly like an absent one: the status line then asks for the key again.")]
    private string? ReadUserScope(string envName)
    {
        try
        {
            return _environment.Read(envName, EnvironmentVariableTarget.User);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
