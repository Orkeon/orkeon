using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.Security.Secrets;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Security.Secrets;

public class DpapiSecretProviderTestsFixture
{
    private readonly ILogger<DpapiSecretProvider> _logger = NullLogger<DpapiSecretProvider>.Instance;

    public DpapiSecretProvider Build(FakeFileSystemService fs, string vDir = "/secrets")
        => new(fs, vDir, _logger);

    public ILogger<DpapiSecretProvider> GetLogger() => _logger;
}
