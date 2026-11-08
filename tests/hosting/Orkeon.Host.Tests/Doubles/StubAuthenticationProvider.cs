using System.Security.Claims;
using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Host.Tests.Doubles;

/// <summary>A bearer validator that accepts one token.</summary>
internal sealed class StubAuthenticationProvider(string validToken) : IAuthenticationProvider
{
    public string ProviderName => "stub";

    public Task<AuthenticationResult> AuthenticateAsync(string token, CancellationToken ct = default) =>
        Task.FromResult(token == validToken
            ? new AuthenticationResult(true, new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "client")], "stub")), null)
            : new AuthenticationResult(false, null, "rejected"));

    public Task<TokenValidationResult> ValidateTokenAsync(string token, CancellationToken ct = default) =>
        Task.FromResult(new TokenValidationResult(token == validToken, null, null));

    public Task<ClaimsPrincipal?> GetPrincipalAsync(string token, CancellationToken ct = default) =>
        Task.FromResult<ClaimsPrincipal?>(null);
}
