using System.Diagnostics;

namespace Orkeon.Tools.Code.Sessions;

/// <summary>The environment a shell session, and every command it runs, starts with.</summary>
internal static class ShellEnvironment
{
    /// <summary>
    /// The only environment variables handed to the session. Everything else the runner holds is
    /// dropped, because that environment carries the crew's LLM API keys
    /// (<c>ORKEON_Llm__ApiKey</c> and friends) and an allowed read-only command
    /// (<c>cat /proc/self/environ</c>, <c>set</c>) would otherwise read them straight back into the
    /// model's context.
    /// <para>
    /// Twin of <c>ProcessIsolationSandbox.ConfigureRestrictedEnvironment</c>
    /// (<c>src/core/Orkeon.Infrastructure/Sandbox</c>), which does the same for the code sandbox. The
    /// list is duplicated rather than shared: a tool project cannot reference Infrastructure, and the
    /// shared kernel it could go through (<c>Orkeon.Tools.Abstractions</c>) has no process-execution
    /// surface to hang it on.
    /// </para>
    /// </summary>
    private static readonly string[] s_propagatedEnvironmentVariables =
    [
        // PATH is propagated, not rebuilt: the tool starts its command from a BARE executable
        // name by design, so the host lookup path is what makes git and the opt-in
        // interpreters resolvable wherever an operator installed them.
        "PATH",
        // POSIX: git needs a HOME to find its own configuration, the locale decides how the
        // child encodes what it prints, and TMPDIR gives it a writable scratch directory.
        "HOME", "LANG", "LC_ALL", "TMPDIR",
        // Windows: cmd.exe and the CLR resolve an executable through these.
        "USERPROFILE", "SystemRoot", "SystemDrive", "PATHEXT", "TEMP", "TMP",
        "APPDATA", "LOCALAPPDATA", "ProgramFiles", "ProgramFiles(x86)",
        // The .NET and NuGet locations, load-bearing once interpreters are opted in.
        "DOTNET_ROOT", "NUGET_PACKAGES"
    ];

    /// <summary>
    /// Replaces the inherited environment of <paramref name="startInfo"/> with the propagated
    /// variables, so a command the allowlist permits can read its own environment without reading
    /// the runner's secrets.
    /// </summary>
    public static void ConfigureRestricted(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        startInfo.Environment.Clear();
        foreach (var (name, value) in RestrictedVariables())
            startInfo.Environment[name] = value;
    }

    /// <summary>
    /// The variables a session starts with: the propagated ones the host has, then the two that
    /// keep <c>dotnet</c> quiet. The keys compare without case, as Windows does.
    /// </summary>
    public static Dictionary<string, string> RestrictedVariables()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in s_propagatedEnvironmentVariables)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
                variables[name] = value;
        }

        // Set, not propagated: the child must stay quiet and must not start a telemetry
        // upload of its own on the first `dotnet` invocation of a crew run.
        variables["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        variables["DOTNET_NOLOGO"] = "1";
        return variables;
    }
}
