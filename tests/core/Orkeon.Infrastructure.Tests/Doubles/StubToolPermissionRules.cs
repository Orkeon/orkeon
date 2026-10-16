using System.Collections.Immutable;
using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>An <see cref="IToolPermissionRules"/> holding the rules a test sets in <see cref="Current"/>.</summary>
public sealed class StubToolPermissionRules : IToolPermissionRules
{
    /// <inheritdoc />
    public ImmutableList<ToolPermissionRule> Current { get; set; } = [];

    /// <summary>A rule allowing <paramref name="tool"/>, or the calls whose <paramref name="argument"/> matches <paramref name="pattern"/>.</summary>
    public StubToolPermissionRules Allow(string tool, string? argument = null, string? pattern = null)
        => With(ToolPermissionEffect.Allow, tool, argument, pattern);

    /// <summary>A rule denying <paramref name="tool"/>, or the calls whose <paramref name="argument"/> matches <paramref name="pattern"/>.</summary>
    public StubToolPermissionRules Deny(string tool, string? argument = null, string? pattern = null)
        => With(ToolPermissionEffect.Deny, tool, argument, pattern);

    private StubToolPermissionRules With(ToolPermissionEffect effect, string tool, string? argument, string? pattern)
    {
        Current = Current.Add(new ToolPermissionRule { Effect = effect, ToolName = tool, Argument = argument, Pattern = pattern });
        return this;
    }
}
