using Orkeon.Domain.Tools.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Security;

namespace Orkeon.Infrastructure.Tests.Security;

public class PathValidatorTestsFixture
{
    private readonly ILogger<PathValidator> _logger = NullLogger<PathValidator>.Instance;
    private PathSecurityOptions _options = new()
    {
        DefaultWorkspaceRoot = "/workspace"
    };

    // --- Fluent configuration ---

    public PathValidatorTestsFixture WithWorkspaceRoot(string root)
    {
        _options.DefaultWorkspaceRoot = root;
        return this;
    }

    public PathValidatorTestsFixture WithAdditionalBlockedExtensions(params string[] extensions)
    {
        _options.AdditionalBlockedExtensions.Clear();
        foreach (var extension in extensions)
            _options.AdditionalBlockedExtensions.Add(extension);
        return this;
    }

    public PathValidatorTestsFixture WithAdditionalAllowedDirectories(params string[] directories)
    {
        _options.AdditionalAllowedDirectories.Clear();
        foreach (var directory in directories)
            _options.AdditionalAllowedDirectories.Add(directory);
        return this;
    }

    public PathValidatorTestsFixture WithOptions(PathSecurityOptions options)
    {
        _options = options;
        return this;
    }

    // --- Build / Execution ---

    public PathValidator Build()
        => new(_options, _logger);

    public PathValidationResult ValidatePath(string path, string? workspaceRootOverride = null)
        => Build().ValidatePath(path, workspaceRootOverride);

    // --- Inspection ---

    public PathSecurityOptions GetOptions() => _options;
}
