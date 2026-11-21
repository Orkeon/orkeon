using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.Tools;

namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>
/// A registry that refuses one replacement (#445): the replacement of number <see cref="RefuseReplacementNumber"/>
/// (1-based) answers false, as when the registry changed under a tool; every other call goes to a real
/// <see cref="ToolRegistry"/>.
/// </summary>
public sealed class FakeToolRegistry : IToolRegistry
{
    private readonly ToolRegistry _inner;
    private int _replacements;

    /// <summary>Builds the registry over <paramref name="tools"/>.</summary>
    public FakeToolRegistry(IEnumerable<IBaseTool> tools) => _inner = new ToolRegistry(tools);

    /// <summary>The replacement refused, counted from 1; none when 0.</summary>
    public int RefuseReplacementNumber { get; set; }

    /// <summary>The replacements asked, refused ones included.</summary>
    public int Replacements => _replacements;

    /// <inheritdoc />
    public Task<bool> RegisterToolAsync(IBaseTool tool, CancellationToken cancellationToken = default) => _inner.RegisterToolAsync(tool, cancellationToken);

    /// <inheritdoc />
    public Task<bool> UnregisterToolAsync(string toolId, CancellationToken cancellationToken = default) => _inner.UnregisterToolAsync(toolId, cancellationToken);

    /// <inheritdoc />
    public Task<IBaseTool?> GetToolAsync(string toolId, CancellationToken cancellationToken = default) => _inner.GetToolAsync(toolId, cancellationToken);

    /// <inheritdoc />
    public Task<IBaseTool?> GetToolByNameAsync(string name, CancellationToken cancellationToken = default) => _inner.GetToolByNameAsync(name, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<IBaseTool>> GetAllToolsAsync(CancellationToken cancellationToken = default) => _inner.GetAllToolsAsync(cancellationToken);

    /// <inheritdoc />
    public Task<bool> IsRegisteredAsync(string toolId, CancellationToken cancellationToken = default) => _inner.IsRegisteredAsync(toolId, cancellationToken);

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken cancellationToken = default) => _inner.ClearAsync(cancellationToken);

    /// <inheritdoc />
    public Task<bool> ReplaceToolAsync(IBaseTool current, IBaseTool replacement, CancellationToken cancellationToken = default) =>
        Interlocked.Increment(ref _replacements) == RefuseReplacementNumber
            ? Task.FromResult(false)
            : _inner.ReplaceToolAsync(current, replacement, cancellationToken);
}
