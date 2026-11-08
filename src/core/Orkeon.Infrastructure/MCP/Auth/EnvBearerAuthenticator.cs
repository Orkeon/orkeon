using System.Net.Http.Headers;

namespace Orkeon.Infrastructure.MCP.Auth;

/// <summary>
/// Adds <c>Authorization: Bearer</c> with the token read from an environment variable on every
/// request, so a rotated token is picked up without a restart. A variable that is empty at request
/// time fails the request: a call is never sent unauthenticated.
/// </summary>
internal sealed class EnvBearerAuthenticator(string key, string variable, Func<string, string?> environment) : IMcpRequestAuthenticator
{
    public ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var token = environment(variable);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new McpServerConfigurationException(key,
                $"{key} reads the environment variable '{variable}', which is empty: the request was not sent.");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return ValueTask.CompletedTask;
    }
}
