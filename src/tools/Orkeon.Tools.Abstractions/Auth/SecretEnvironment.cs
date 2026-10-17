using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Tools.Abstractions.Auth;

/// <summary>
/// Where the variable an account names for its password or its client secret is read
/// (STUDIO-68): the process environment, then the user's persistent scope —
/// <c>HKCU\Environment</c> on Windows, where Orkeon Studio remembers a secret. Linux and macOS
/// have no such scope, and <see cref="User"/> is then null. Read only, never written: a secret
/// found in the user scope is not copied into the process, so what a run starts — a shell tool,
/// an MCP server — inherits no secret it did not have. The seam of Infrastructure's
/// <c>LlmKeyEnvironment</c>, spelt again because the tool families do not reference it; replaced
/// in tests, which never touch the machine's environment.
/// </summary>
/// <param name="Process">Reads a variable of the process environment by name.</param>
/// <param name="User">Reads a variable of the user's persistent scope by name, and may throw when the scope cannot be read; null on a machine that has none.</param>
internal sealed record SecretEnvironment(Func<string, string?> Process, Func<string, string?>? User = null)
{
    /// <summary>The machine's own environment.</summary>
    public static SecretEnvironment Machine { get; } = new(
        Environment.GetEnvironmentVariable,
        OperatingSystem.IsWindows() ? name => Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User) : null);

    /// <summary>What a failure says of a variable found nowhere: the places it was looked for.</summary>
    public string Unset => User is null ? "is not set" : "is set neither in the process environment nor in the user's";

    /// <summary>The first non-empty value of <paramref name="name"/>, the process before the user scope; null when neither holds one.</summary>
    public string? Read(string name)
    {
        if (Process(name) is { Length: > 0 } inProcess)
            return inProcess;
        return ReadUserScope(name) is { Length: > 0 } inUserScope ? inUserScope : null;
    }

    /// <summary>
    /// The user scope, read only. A scope that cannot be read — the registry refused, a service
    /// account without a profile — is a variable not found: the call then fails as it does
    /// without a secret, and the readiness check says which variable is missing.
    /// </summary>
    [SuppressMessage("Design", "CA1031",
        Justification = "Whatever reading the user scope throws, the variable is not found there — exactly " +
                        "like an absent one; the account then says which variable it could not read.")]
    private string? ReadUserScope(string name)
    {
        try
        {
            return User?.Invoke(name);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
