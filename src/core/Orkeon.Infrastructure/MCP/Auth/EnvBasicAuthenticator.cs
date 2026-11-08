using System.Net.Http.Headers;
using System.Text;

namespace Orkeon.Infrastructure.MCP.Auth;

/// <summary>
/// Adds <c>Authorization: Basic</c> from two environment variables, read on every request. A variable
/// that is empty at request time fails the request.
/// </summary>
internal sealed class EnvBasicAuthenticator(
    string userKey, string userVariable, string passwordKey, string passwordVariable, Func<string, string?> environment)
    : IMcpRequestAuthenticator
{
    public ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = Read(userKey, userVariable);
        var password = Read(passwordKey, passwordVariable);
        var raw = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", raw);
        return ValueTask.CompletedTask;
    }

    private string Read(string key, string variable)
    {
        var value = environment(variable);
        if (string.IsNullOrEmpty(value))
        {
            throw new McpServerConfigurationException(key,
                $"{key} reads the environment variable '{variable}', which is empty: the request was not sent.");
        }

        return value;
    }
}
