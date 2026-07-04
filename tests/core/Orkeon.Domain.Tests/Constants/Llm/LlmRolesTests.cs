using Orkeon.Domain.Constants.Llm;

namespace Orkeon.Domain.Tests.Constants.Llm;

/// <summary>
/// SML-009 / R12.5: the single normalized role-matching helper. Roles are compared
/// case-insensitively so a mixed-case role is handled identically on every path.
/// </summary>
public class LlmRolesTests
{
    [Theory]
    [InlineData("assistant")]
    [InlineData("Assistant")]
    [InlineData("ASSISTANT")]
    public void IsAssistant_ShouldMatch_RegardlessOfCase(string role)
        => Assert.True(LlmRoles.IsAssistant(role));

    [Theory]
    [InlineData("tool")]
    [InlineData("Tool")]
    [InlineData("system")]
    [InlineData("System")]
    [InlineData("user")]
    [InlineData("User")]
    public void Is_ShouldMatchCanonical_RegardlessOfCase(string role)
        => Assert.True(LlmRoles.Is(role, role.ToLowerInvariant()));

    [Fact]
    public void Is_ShouldNotMatch_DifferentRole()
        => Assert.False(LlmRoles.IsAssistant(LlmRoles.User));

    [Fact]
    public void Is_ShouldNotMatch_Null()
        => Assert.False(LlmRoles.IsTool(null));

    [Fact]
    public void CanonicalValues_AreLowercaseWireNames()
    {
        Assert.Equal("system", LlmRoles.System);
        Assert.Equal("user", LlmRoles.User);
        Assert.Equal("assistant", LlmRoles.Assistant);
        Assert.Equal("tool", LlmRoles.Tool);
    }
}
