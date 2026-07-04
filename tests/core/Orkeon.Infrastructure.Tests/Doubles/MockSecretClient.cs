using System.Diagnostics.CodeAnalysis;
using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock for Azure SecretClient. Subclasses SecretClient and overrides virtual methods.
/// Azure SDK classes have virtual methods specifically for testability.
/// </summary>
public sealed class MockSecretClient : SecretClient
{
    private Func<string, string?, SecretContentType?, CancellationToken, Task<Response<KeyVaultSecret>>>? _getSecretFunc;

    // --- Tracking ---
    public int GetSecretCallCount { get; private set; }
    public string? LastGetSecretName { get; private set; }

    // --- Configuration ---
    public void SetGetSecretFunc(
        Func<string, string?, SecretContentType?, CancellationToken, Task<Response<KeyVaultSecret>>> func)
        => _getSecretFunc = func;

    public void SetGetSecretResult(string name, KeyVaultSecret secret)
    {
        _getSecretFunc = (n, v, ct, token) =>
        {
            if (n == name)
                return Task.FromResult(Response.FromValue(secret, new MockResponse()));
            throw new RequestFailedException(404, "Not found");
        };
    }

    public void SetGetSecretThrows(string name, RequestFailedException ex)
    {
        var existing = _getSecretFunc;
        _getSecretFunc = (n, v, ct, token) =>
        {
            if (n == name)
                throw ex;
            if (existing != null)
                return existing(n, v, ct, token);
            throw new RequestFailedException(404, "Not found");
        };
    }

    public override Task<Response<KeyVaultSecret>> GetSecretAsync(
        string name,
        string? version = null,
        SecretContentType? contentType = null,
        CancellationToken cancellationToken = default)
    {
        GetSecretCallCount++;
        LastGetSecretName = name;

        if (_getSecretFunc != null)
            return _getSecretFunc(name, version, contentType, cancellationToken);

        throw new NotImplementedException("GetSecretAsync not configured");
    }

    /// <summary>
    /// Minimal Response implementation for testing.
    /// </summary>
    private sealed class MockResponse : Response
    {
        public override int Status => 200;
        public override string ReasonPhrase => "OK";
        public override Stream? ContentStream { get => null; set { } }
        public override string ClientRequestId { get => "test"; set { } }

        public override void Dispose() { }

        protected override bool ContainsHeader(string name) => false;
        protected override IEnumerable<HttpHeader> EnumerateHeaders() => Array.Empty<HttpHeader>();
        protected override bool TryGetHeader(string name, [NotNullWhen(true)] out string? value)
        {
            value = null;
            return false;
        }
        protected override bool TryGetHeaderValues(string name, [NotNullWhen(true)] out IEnumerable<string>? values)
        {
            values = null;
            return false;
        }
    }
}
