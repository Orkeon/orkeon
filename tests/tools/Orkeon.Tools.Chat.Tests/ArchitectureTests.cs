using Orkeon.Tools.Chat.DependencyInjection;

namespace Orkeon.Tools.Chat.Tests;

/// <summary>
/// Guards the layering rule of <c>Orkeon.Tools.Chat</c> (ADR-005, ADR-021): Domain-side
/// assemblies, the tool abstractions, the RAG prompt-injection detector and the shared constants
/// only; never <c>Orkeon.Application</c>, <c>Orkeon.Infrastructure</c> or another tool family, and no vendor SDK.
/// Written as an allowlist, so the next reference costs a deliberate edit here.
/// </summary>
public class ArchitectureTests
{
    private static readonly string[] AllowedOrkeonReferences =
    [
        "Orkeon.Domain",
        "Orkeon.Tools.Abstractions",
        "Orkeon.Rag",
        "Orkeon.Rag.Abstractions",
        "Orkeon.Constants.Configuration",
        "Orkeon.Compliance.Vfs",
    ];

    private static readonly System.Reflection.AssemblyName[] Referenced =
        typeof(ChatToolsServiceCollectionExtensions).Assembly.GetReferencedAssemblies();

    [Fact]
    public void ToolsChat_ReferencesNothingOutsideTheAllowlist()
    {
        var offenders = Referenced
            .Select(a => a.Name ?? string.Empty)
            .Where(name => name.StartsWith("Orkeon.", StringComparison.Ordinal))
            .Where(name => !AllowedOrkeonReferences.Contains(name, StringComparer.Ordinal))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Orkeon.Tools.Chat references " + string.Join(", ", offenders)
            + " — outside the layering allowlist. Add it deliberately, or route through an abstraction.");
    }

    /// <summary>
    /// The ones the allowlist exists to keep out, named explicitly: the compiler prunes unused
    /// references from metadata, so an allowlist alone would pass for the wrong reason.
    /// </summary>
    [Theory]
    [InlineData("Orkeon.Application")]
    [InlineData("Orkeon.Infrastructure")]
    [InlineData("Orkeon.Tools.Email")]
    [InlineData("Orkeon.Tools.Drive")]
    [InlineData("Microsoft.Graph")]
    [InlineData("Microsoft.Identity.Client")]
    public void ToolsChat_ShouldNotReference(string forbidden)
    {
        Assert.DoesNotContain(Referenced, a => a.Name == forbidden);
    }

    [Theory]
    [InlineData("Orkeon.Domain")]
    [InlineData("Orkeon.Tools.Abstractions")]
    [InlineData("Orkeon.Rag")]
    public void ToolsChat_ShouldReference(string required)
    {
        Assert.Contains(Referenced, a => a.Name == required);
    }

    [Fact]
    public void ToolsChat_ShouldKeepItsInternalsInternal()
    {
        var exported = typeof(ChatToolsServiceCollectionExtensions).Assembly.GetExportedTypes()
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
        [
            "Orkeon.Tools.Chat.Accounts.ChatRightsCheck",
            "Orkeon.Tools.Chat.Administration.ChatAccountAdministration",
            "Orkeon.Tools.Chat.Administration.ChatAccountStatus",
            "Orkeon.Tools.Chat.Administration.ChatCheckResult",
            "Orkeon.Tools.Chat.Administration.ChatLogoutResult",
            "Orkeon.Tools.Chat.Administration.ChatRightStatus",
            "Orkeon.Tools.Chat.Auth.ChatTokenSet",
            "Orkeon.Tools.Chat.Auth.FileSystemChatTokenStore",
            "Orkeon.Tools.Chat.Auth.IChatTokenStore",
            "Orkeon.Tools.Chat.ChatErrorCode",
            "Orkeon.Tools.Chat.ChatToolException",
            "Orkeon.Tools.Chat.Configuration.ChatCredentialsLocation",
            "Orkeon.Tools.Chat.DependencyInjection.ChatToolsServiceCollectionExtensions",
        ], exported);
    }
}
