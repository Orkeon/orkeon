using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Security.Auth;
using Microsoft.IdentityModel.Tokens;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;

namespace Orkeon.Infrastructure.Tests.Security.Auth;

/// <summary>
/// Helper to create test JWTs signed with a known symmetric key.
/// </summary>
internal static class TestJwtHelper
{
    public static readonly SymmetricSecurityKey SigningKey =
        new(System.Text.Encoding.UTF8.GetBytes("ThisIsATestSigningKeyThatIsLongEnoughForHmacSha256!"));

    public static readonly SigningCredentials SigningCredentials =
        new(SigningKey, SecurityAlgorithms.HmacSha256);

    public const string Issuer = "https://test-issuer.example.com";
    public const string Audience = "test-client-id";

    public static string CreateToken(
        IEnumerable<Claim>? additionalClaims = null,
        DateTime? expires = null,
        DateTime? notBefore = null,
        string? issuer = null,
        string? audience = null,
        SigningCredentials? credentials = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "test-user-id"),
            new(JwtRegisteredClaimNames.Email, "test@example.com"),
            new("name", "Test User")
        };

        if (additionalClaims != null)
            claims.AddRange(additionalClaims);

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims, "TestAuth"),
            Expires = expires ?? DateTime.UtcNow.AddHours(1),
            NotBefore = notBefore ?? DateTime.UtcNow.AddMinutes(-1),
            Issuer = issuer ?? Issuer,
            Audience = audience ?? Audience,
            SigningCredentials = credentials ?? SigningCredentials
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(descriptor);
        return handler.WriteToken(token);
    }

    public static TokenValidationParameters CreateValidationParameters(
        string? issuer = null,
        string? audience = null,
        SecurityKey? signingKey = null)
    {
        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer ?? Issuer,
            ValidateAudience = true,
            ValidAudience = audience ?? Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey ?? SigningKey,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    }
}

public class AzureAdAuthProviderTests
{
    [Fact]
    public async Task ValidToken_ReturnsAuthenticated()
    {
        var token = TestJwtHelper.CreateToken();
        var provider = new AzureAdAuthProvider(TestJwtHelper.CreateValidationParameters());

        var result = await provider.AuthenticateAsync(token, TestContext.Current.CancellationToken);

        Assert.True(result.IsAuthenticated);
        Assert.NotNull(result.Principal);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ExpiredToken_ReturnsNotAuthenticated()
    {
        var token = TestJwtHelper.CreateToken(
            expires: DateTime.UtcNow.AddHours(-1),
            notBefore: DateTime.UtcNow.AddHours(-3));
        var provider = new AzureAdAuthProvider(TestJwtHelper.CreateValidationParameters());

        var result = await provider.AuthenticateAsync(token, TestContext.Current.CancellationToken);

        Assert.False(result.IsAuthenticated);
        Assert.Null(result.Principal);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task InvalidSignature_ReturnsNotAuthenticated()
    {
        var wrongKey = new SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes("DifferentKeyThatDoesNotMatchTheExpectedSigningKey!!"));
        var wrongCredentials = new SigningCredentials(wrongKey, SecurityAlgorithms.HmacSha256);

        var token = TestJwtHelper.CreateToken(credentials: wrongCredentials);
        var provider = new AzureAdAuthProvider(TestJwtHelper.CreateValidationParameters());

        var result = await provider.AuthenticateAsync(token, TestContext.Current.CancellationToken);

        Assert.False(result.IsAuthenticated);
        Assert.Null(result.Principal);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ExtractsClaims_Correctly()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim("department", "Engineering")
        };

        var token = TestJwtHelper.CreateToken(additionalClaims: claims);
        var provider = new AzureAdAuthProvider(TestJwtHelper.CreateValidationParameters());

        var result = await provider.AuthenticateAsync(token, TestContext.Current.CancellationToken);

        Assert.True(result.IsAuthenticated);
        var principal = result.Principal!;

        // JsonWebTokenHandler uses short claim names (not ClaimTypes URIs)
        Assert.Contains(principal.Claims, c => c.Value == "Admin" &&
            (c.Type == ClaimTypes.Role || c.Type == "role"));
        Assert.Contains(principal.Claims, c => c.Type == "department" && c.Value == "Engineering");
        Assert.Contains(principal.Claims, c => c.Type == "name" && c.Value == "Test User");
    }

    [Fact]
    public async Task ValidateTokenAsync_ValidToken_ReturnsValid()
    {
        var token = TestJwtHelper.CreateToken();
        var provider = new AzureAdAuthProvider(TestJwtHelper.CreateValidationParameters());

        var result = await provider.ValidateTokenAsync(token, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.Null(result.Error);
        Assert.NotNull(result.ExpiresAt);
    }

    [Fact]
    public async Task GetPrincipalAsync_ValidToken_ReturnsPrincipal()
    {
        var token = TestJwtHelper.CreateToken();
        var provider = new AzureAdAuthProvider(TestJwtHelper.CreateValidationParameters());

        var principal = await provider.GetPrincipalAsync(token, TestContext.Current.CancellationToken);

        Assert.NotNull(principal);
        Assert.True(principal.Identity?.IsAuthenticated);
    }

    [Fact]
    public void ProviderName_IsAzureAD()
    {
        var provider = new AzureAdAuthProvider(TestJwtHelper.CreateValidationParameters());
        Assert.Equal("AzureAD", provider.ProviderName);
    }
}

public class OidcAuthProviderTests
{
    [Fact]
    public async Task ValidToken_ReturnsAuthenticated()
    {
        var token = TestJwtHelper.CreateToken();
        var options = new OidcOptions { Authority = "https://test.example.com", ClientId = TestJwtHelper.Audience };
        var provider = new OidcAuthProvider(options, TestJwtHelper.CreateValidationParameters());

        var result = await provider.AuthenticateAsync(token, TestContext.Current.CancellationToken);

        Assert.True(result.IsAuthenticated);
        Assert.NotNull(result.Principal);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task InvalidToken_ReturnsNotAuthenticated()
    {
        var token = TestJwtHelper.CreateToken(expires: DateTime.UtcNow.AddHours(-1), notBefore: DateTime.UtcNow.AddHours(-3));
        var options = new OidcOptions { Authority = "https://test.example.com", ClientId = TestJwtHelper.Audience };
        var provider = new OidcAuthProvider(options, TestJwtHelper.CreateValidationParameters());

        var result = await provider.AuthenticateAsync(token, TestContext.Current.CancellationToken);

        Assert.False(result.IsAuthenticated);
        Assert.Null(result.Principal);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ProviderName_IsOIDC()
    {
        var options = new OidcOptions { Authority = "https://test.example.com", ClientId = "client" };
        var provider = new OidcAuthProvider(options, TestJwtHelper.CreateValidationParameters());
        Assert.Equal("OIDC", provider.ProviderName);
    }
}
