using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Orkeon.Host.Gateway;
using Orkeon.Tests.Shared.Network;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Host.Tests.Doubles;

/// <summary>
/// Microsoft's side of the Teams channel: the metadata and its keys, the sign-in service and the connector, behind one
/// transport, and the tokens the Bot Connector signs, each with a key generated for the test.
/// </summary>
internal sealed class FakeBotConnector : IDisposable
{
    public const string AppId = "11111111-2222-3333-4444-555555555555";
    public const string TenantId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    public const string ServiceUrl = "https://smba.trafficmanager.net/emea/";
    public const string KeysAddress = "https://login.botframework.com/v1/.well-known/keys";
    public const string SignInHost = "https://login.microsoftonline.com/";

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly List<RSA> _others = [];

    public FakeBotConnector()
    {
        Transport.Map(HttpMethod.Get, BotConnectorKeys.MetadataAddress, _ =>
        {
            BeforeMetadata();
            return Metadata;
        });
        Transport.Map(HttpMethod.Get, KeysAddress, _ => FakeHttpResponse.Json(KeysDocument()));
        Transport.Map(HttpMethod.Post, SignInHost, _ => SignIn);
        Transport.Map(HttpMethod.Post, "https://smba.", _ => Connector());
        Environment.SetEnvironmentVariable(SecretVariable, Secret);
    }

    public FakeHttpMessageHandler Transport { get; } = new();
    public ManualTimeProvider Time { get; } = new(new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero));
    public string SecretVariable { get; } = $"ORKEON_TEST_TEAMS_{Guid.NewGuid():N}";
    public string Secret { get; } = $"hush-{Guid.NewGuid():N}";
    public string KeyId { get; set; } = "key-1";
    public string[] Endorsements { get; set; } = ["msteams"];
    public FakeHttpResponse Metadata { get; set; } = FakeHttpResponse.Json(MetadataNaming(KeysAddress));
    public Action BeforeMetadata { get; set; } = () => { };
    public FakeHttpResponse SignIn { get; set; } = FakeHttpResponse.Json("""{"token_type":"Bearer","expires_in":3600,"access_token":"outbound"}""");
    public Func<FakeHttpResponse> Connector { get; set; } = () => new FakeHttpResponse(HttpStatusCode.Created, """{"id":"reply"}""");

    public IReadOnlyList<RecordedRequest> MetadataReads => Transport.RequestsTo(HttpMethod.Get, BotConnectorKeys.MetadataAddress);
    public IReadOnlyList<RecordedRequest> SignIns => Transport.RequestsTo(HttpMethod.Post, SignInHost);
    public IReadOnlyList<RecordedRequest> Replies => Transport.RequestsTo(HttpMethod.Post, ServiceUrl);

    public static string MetadataNaming(string keysAddress, string issuer = BotConnectorKeys.Issuer) =>
        JsonSerializer.Serialize(new { issuer, jwks_uri = keysAddress });

    public TeamsChannelOptions Options(string prefix = TeamsChannelOptions.DefaultPrefix) => new()
    {
        Enabled = true,
        AppId = AppId,
        TenantId = TenantId,
        SecretEnvironmentVariable = SecretVariable,
        AllowedUserIds = [Activities.UserId],
        Prefix = prefix,
    };

    /// <summary>A token of the Bot Connector, unless an argument says what is wrong with it.</summary>
    public string Token(
        string issuer = BotConnectorKeys.Issuer,
        string audience = AppId,
        object? serviceUrl = null,
        bool withServiceUrl = true,
        string? keyId = null,
        RSA? signedWith = null,
        string algorithm = SecurityAlgorithms.RsaSha256,
        TimeSpan? notBefore = null,
        TimeSpan? expires = null,
        bool withExpiry = true)
    {
        var now = Time.GetUtcNow();
        var claims = new Dictionary<string, object> { ["iss"] = issuer, ["aud"] = audience };
        if (withServiceUrl)
            claims["serviceurl"] = serviceUrl ?? ServiceUrl;
        claims["nbf"] = (now + (notBefore ?? TimeSpan.FromMinutes(-1))).ToUnixTimeSeconds();
        if (withExpiry)
            claims["exp"] = (now + (expires ?? TimeSpan.FromHours(1))).ToUnixTimeSeconds();

        var key = new RsaSecurityKey(signedWith ?? _rsa) { KeyId = keyId ?? KeyId };
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }
            .CreateToken(JsonSerializer.Serialize(claims), new SigningCredentials(key, algorithm));
    }

    /// <summary>A key the metadata does not list.</summary>
    public RSA AnotherKey()
    {
        var rsa = RSA.Create(2048);
        _others.Add(rsa);
        return rsa;
    }

    /// <summary>The public half of the signing key, as anyone can read it: what an HS256 forgery signs with.</summary>
    public byte[] PublicKey() => _rsa.ExportSubjectPublicKeyInfo();

    private string KeysDocument()
    {
        var key = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(_rsa.ExportParameters(false)) { KeyId = KeyId });
        return JsonSerializer.Serialize(new
        {
            keys = new[] { new { kty = "RSA", use = "sig", kid = KeyId, n = key.N, e = key.E, endorsements = Endorsements } },
        });
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(SecretVariable, null);
        Transport.Dispose();
        _rsa.Dispose();
        foreach (var other in _others)
            other.Dispose();
    }
}

/// <summary>The activities Teams posts.</summary>
internal static class Activities
{
    public const string UserId = "99999999-8888-7777-6666-555555555555";
    public const string BotId = "28:bot";
    public const string Room = "19:room@thread.tacv2";
    public const string Thread = Room + ";messageid=1700";

    /// <summary>A message mentioning the bot in a channel's thread, each field one a test may change.</summary>
    public static Dictionary<string, object?> Message(string text = "<at>Orkeon</at> hello", string id = "1701", string conversation = Thread) => new()
    {
        ["type"] = "message",
        ["id"] = id,
        ["channelId"] = "msteams",
        ["serviceUrl"] = FakeBotConnector.ServiceUrl,
        ["from"] = new { id = "29:user", aadObjectId = UserId },
        ["recipient"] = new { id = BotId, name = "Orkeon" },
        ["conversation"] = new { id = conversation, conversationType = "channel", tenantId = FakeBotConnector.TenantId },
        ["channelData"] = new { tenant = new { id = FakeBotConnector.TenantId } },
        ["entities"] = new object[] { new { type = "mention", text = "<at>Orkeon</at>", mentioned = new { id = BotId, name = "Orkeon" } } },
        ["text"] = text,
    };
}
