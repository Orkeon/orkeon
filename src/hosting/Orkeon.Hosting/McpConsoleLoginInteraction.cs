using Orkeon.Tools.Abstractions.Auth;

namespace Orkeon.Hosting;

/// <summary>
/// The sign-in conversation of an <c>oauth2-device</c> MCP server on the runner's terminal (FWK-16): it shows the
/// address and the code on <see cref="IRunnerConsole.ErrorOutput"/>, so a piped standard output stays the run's
/// product. The redirect steps of the loopback flow are never used by an MCP server.
/// </summary>
/// <param name="console">The terminal the code is shown on.</param>
internal sealed class McpConsoleLoginInteraction(IRunnerConsole console) : IOAuthLoginInteraction
{
    public Task ShowDeviceCodeAsync(string account, Uri verificationUri, string userCode, TimeSpan expiresIn, CancellationToken cancellationToken) =>
        console.ErrorOutput.WriteLineAsync(
            $"To sign MCP server '{account}' in, open {verificationUri} in a browser and enter the code {userCode} " +
            $"(valid {Math.Ceiling(expiresIn.TotalMinutes)} min). Waiting for the sign-in to complete...");

    public Task ShowAuthorizationUrlAsync(string account, Uri authorizationUri, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<string?> ReadRedirectAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

    public Task ShowRedirectRejectedAsync(string account, string reason, CancellationToken cancellationToken) => Task.CompletedTask;
}
