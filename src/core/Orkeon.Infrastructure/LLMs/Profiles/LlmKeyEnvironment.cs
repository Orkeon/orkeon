namespace Orkeon.Infrastructure.LLMs.Profiles;

/// <summary>
/// Where <see cref="LlmSettings"/> reads the variable an <c>ApiKeyEnvVar</c> names (STUDIO-49):
/// the process environment, then the user's persistent scope — <c>HKCU\Environment</c> on
/// Windows, where Orkeon Studio remembers a key; .NET reads it as absent elsewhere. Read only,
/// never written: a key found in the user scope is not copied into the process, so what a run
/// starts — a shell tool, an MCP server, the code sandbox — inherits no key it did not have.
/// A seam rather than a static field, as the e-mail tools' <c>EmailEnvironment</c>: the suites
/// run in parallel, and no test sets a variable of the machine.
/// </summary>
/// <param name="Process">Reads a variable of the process environment by name.</param>
/// <param name="User">Reads a variable of the user's persistent scope by name; may throw when the scope cannot be read.</param>
internal sealed record LlmKeyEnvironment(Func<string, string?> Process, Func<string, string?> User)
{
    /// <summary>The machine's own environment.</summary>
    public static LlmKeyEnvironment Machine { get; } = new(
        Environment.GetEnvironmentVariable,
        name => Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User));
}
