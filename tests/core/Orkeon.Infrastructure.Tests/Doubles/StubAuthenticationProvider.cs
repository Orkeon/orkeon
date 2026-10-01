using System.Security.Claims;
using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IAuthenticationProvider"/>: accepts exactly the tokens it was
/// given, refuses every other one, and records the tokens it was asked about.
/// </summary>
public sealed class StubAuthenticationProvider : IAuthenticationProvider
{
    private readonly HashSet<string> _validTokens;

    public StubAuthenticationProvider(params string[] validTokens)
        => _validTokens = new HashSet<string>(validTokens, StringComparer.Ordinal);

    public string ProviderName => "Stub";

    public List<string> SeenTokens { get; } = [];

    public Task<AuthenticationResult> AuthenticateAsync(string token, CancellationToken ct = default)
    {
        SeenTokens.Add(token);
        return Task.FromResult(_validTokens.Contains(token)
            ? new AuthenticationResult(true, new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "peer")], "Stub")), null)
            : new AuthenticationResult(false, null, "unknown token"));
    }

    public Task<TokenValidationResult> ValidateTokenAsync(string token, CancellationToken ct = default)
        => Task.FromResult(new TokenValidationResult(_validTokens.Contains(token), null, null));

    public async Task<ClaimsPrincipal?> GetPrincipalAsync(string token, CancellationToken ct = default)
        => (await AuthenticateAsync(token, ct).ConfigureAwait(false)).Principal;
}
