using Orkeon.Domain.Tools.Security;

namespace Orkeon.Tests.Shared.Doubles;

/// <summary>
/// Hand-rolled <see cref="IPathValidator"/> stub used by tests that previously relied on
/// <c>Mock&lt;IPathValidator&gt;</c>. By default behaves in "strict" mode and throws on
/// any unconfigured call. Configure either a global responder via
/// <see cref="RespondWith(Func{string,string?,PathValidationResult})"/> or per-path mappings
/// via <see cref="WhenPath"/>.
/// </summary>
public sealed class StubPathValidator : IPathValidator
{
    private Func<string, string?, PathValidationResult>? _responder;
    private readonly Dictionary<(string path, string? workspaceRoot), PathValidationResult> _byPath = new();

    /// <summary>Captured invocations of <see cref="ValidatePath"/>.</summary>
    public List<(string Path, string? WorkspaceRoot)> Calls { get; } = new();

    /// <summary>Allow every call, returning <see cref="PathValidationResult.Allowed"/>.</summary>
    public StubPathValidator AllowAll()
    {
        _responder = (p, _) => PathValidationResult.Allowed(p);
        return this;
    }

    /// <summary>Configures a global responder.</summary>
    public StubPathValidator RespondWith(Func<string, string?, PathValidationResult> responder)
    {
        _responder = responder ?? throw new ArgumentNullException(nameof(responder));
        return this;
    }

    /// <summary>Configures the response for a specific (path, workspaceRoot) pair.</summary>
    public StubPathValidator WhenPath(string path, PathValidationResult result, string? workspaceRoot = null)
    {
        _byPath[(path, workspaceRoot)] = result;
        return this;
    }

    /// <inheritdoc />
    public PathValidationResult ValidatePath(string requestedPath, string? workspaceRoot = null)
    {
        Calls.Add((requestedPath, workspaceRoot));

        if (_byPath.TryGetValue((requestedPath, workspaceRoot), out var preset))
            return preset;

        if (_responder is not null)
            return _responder(requestedPath, workspaceRoot);

        throw new InvalidOperationException(
            $"StubPathValidator received unexpected call for path '{requestedPath}'.");
    }
}
