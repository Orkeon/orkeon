using System.Buffers.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace Orkeon.Tools.Abstractions.Auth;

/// <summary>
/// What a client proves its identity with instead of a secret (RFC 7523 section 2.2): a JWT it signs
/// with its certificate, or a token another identity provider issued for it (workload identity
/// federation). It holds text its caller read: this layer opens no file.
/// </summary>
internal sealed class OAuthClientAssertion
{
    /// <summary>Microsoft recommends five to ten minutes at most; one exchange needs far less.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly string? _pem;
    private readonly string? _token;

    private OAuthClientAssertion(string? pem, string? token)
    {
        _pem = pem;
        _token = token;
    }

    /// <summary>
    /// An assertion signed, for each request, with the certificate and the unencrypted RSA private key
    /// of <paramref name="pem"/>. The client's own certificate comes first when there are several.
    /// </summary>
    /// <exception cref="OAuthException">The text holds no certificate with its RSA key.</exception>
    public static OAuthClientAssertion FromCertificatePem(string pem)
    {
        // Read now, so that a wrong text is said when the credential is built and not at its first use.
        using var certificate = Load(pem);
        using var key = PrivateKey(certificate);
        return new OAuthClientAssertion(pem, token: null);
    }

    /// <summary>
    /// A ready assertion: the federated token's text. Such a token is short-lived and rotated by its
    /// issuer, so the caller reads it again for each request.
    /// </summary>
    /// <exception cref="OAuthException">The text is empty.</exception>
    public static OAuthClientAssertion FromFederatedToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new OAuthException(OAuthFailure.InvalidConfiguration, "The federated token is empty.");
        return new OAuthClientAssertion(pem: null, token.Trim());
    }

    /// <summary>The assertion to send to the token endpoint of <paramref name="settings"/>: a credential while it lives.</summary>
    internal string CreateToken(OAuthSettings settings, DateTimeOffset now)
    {
        if (_pem is null)
            return _token!;

        using var certificate = Load(_pem);
        using var key = PrivateKey(certificate);
        // The Microsoft identity platform documents PS256 and the SHA-256 thumbprint.
        var header = new Dictionary<string, object>
        {
            ["alg"] = "PS256",
            ["typ"] = "JWT",
            ["x5t#S256"] = Base64Url.EncodeToString(certificate.GetCertHash(HashAlgorithmName.SHA256)),
        };
        var issued = now.ToUnixTimeSeconds();
        var claims = new Dictionary<string, object>
        {
            ["aud"] = settings.TokenEndpoint.AbsoluteUri,
            ["iss"] = settings.ClientId,
            ["sub"] = settings.ClientId,
            ["jti"] = Guid.NewGuid().ToString(),
            ["nbf"] = issued,
            ["iat"] = issued,
            ["exp"] = now.Add(Lifetime).ToUnixTimeSeconds(),
        };
        var signed = $"{Encode(header)}.{Encode(claims)}";
        byte[] signature;
        try
        {
            signature = key.SignData(Encoding.ASCII.GetBytes(signed), HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        }
        catch (CryptographicException ex)
        {
            throw new OAuthException(OAuthFailure.InvalidConfiguration, "The certificate's private key cannot sign the client assertion.", ex);
        }

        return $"{signed}.{Base64Url.EncodeToString(signature)}";
    }

    private static string Encode(Dictionary<string, object> part) =>
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(part));

    private static X509Certificate2 Load(string pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
            throw new OAuthException(OAuthFailure.InvalidConfiguration, "The client certificate is empty.");
        try
        {
            return X509Certificate2.CreateFromPem(pem, pem);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            throw new OAuthException(OAuthFailure.InvalidConfiguration,
                "The client certificate is not a PEM text holding a certificate and its own unencrypted private key.", ex);
        }
    }

    private static RSA PrivateKey(X509Certificate2 certificate) =>
        certificate.GetRSAPrivateKey()
        ?? throw new OAuthException(OAuthFailure.InvalidConfiguration, "The client certificate's key is not an RSA key, which the signed assertion needs.");
}
