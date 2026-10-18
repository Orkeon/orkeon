using Orkeon.Tools.Email.Administration;

namespace Orkeon.Scripting.Cli.Commands;

/// <summary>
/// The terminal side of an interactive OAuth sign-in, shared by <c>orkeon email login</c> and
/// <c>orkeon drive login</c>: the same conversation, naming the kind of account it signs in.
/// </summary>
/// <param name="family">How the sentences name the account: <c>e-mail</c>, <c>drive</c>.</param>
internal sealed class ConsoleOAuthLoginInteraction(string family) : IEmailLoginInteraction
{
    public Task ShowDeviceCodeAsync(string account, Uri verificationUri, string userCode, TimeSpan expiresIn, CancellationToken cancellationToken) =>
        Console.Out.WriteLineAsync(
            $"To sign {family} account '{account}' in, open {verificationUri} in a browser and enter the code {userCode}.{Environment.NewLine}Waiting for the sign-in to complete (Ctrl+C to abort)…");

    public Task ShowAuthorizationUrlAsync(string account, Uri authorizationUri, CancellationToken cancellationToken) =>
        Console.Out.WriteLineAsync(
            $"To sign {family} account '{account}' in, open this address in a browser:{Environment.NewLine}{Environment.NewLine}  {authorizationUri}{Environment.NewLine}{Environment.NewLine}" +
            "If the browser cannot reach this machine (WSL, a container, a remote shell), paste here the address it ends on (http://127.0.0.1:…) and press Enter.");

    public async Task<string?> ReadRedirectAsync(CancellationToken cancellationToken)
    {
        var line = await Console.In.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        return line;
    }

    public Task ShowRedirectRejectedAsync(string account, string reason, CancellationToken cancellationToken) =>
        Console.Out.WriteLineAsync(
            $"That is not the address the browser ended on: {reason} Paste the complete address (http://127.0.0.1:…) and press Enter.");
}
