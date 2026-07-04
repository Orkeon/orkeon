using Orkeon.Application.Interfaces.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.Security.Secrets;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.Security.Secrets;

public class ChainedSecretProviderTestsFixture
{
    private readonly ILogger<ChainedSecretProvider> _logger = NullLogger<ChainedSecretProvider>.Instance;
    private readonly List<MockSecretProvider> _providers = [];

    // --- Fluent configuration ---

    public ChainedSecretProviderTestsFixture WithProvider(MockSecretProvider provider)
    {
        _providers.Add(provider);
        return this;
    }

    public ChainedSecretProviderTestsFixture WithProvider(Action<MockSecretProvider> configure)
    {
        var provider = new MockSecretProvider();
        configure(provider);
        _providers.Add(provider);
        return this;
    }

    public ChainedSecretProviderTestsFixture WithEmptyProvider()
    {
        _providers.Add(new MockSecretProvider());
        return this;
    }

    // --- Build ---

    public ChainedSecretProvider Build()
        => new(_providers.ToArray(), _logger);

    // --- Execution ---

    public async Task<SecretValue> GetSecretAsync(string key)
        => await Build().GetSecretAsync(key);

    public async Task<bool> ExistsAsync(string key)
        => await Build().ExistsAsync(key);

    public async Task<IReadOnlyList<string>> ListSecretNamesAsync()
        => await Build().ListSecretNamesAsync();

    // --- Inspection ---

    public IReadOnlyList<MockSecretProvider> GetProviders() => _providers;
}
