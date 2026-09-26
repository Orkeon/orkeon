using Orkeon.Tools.Email.DependencyInjection;

namespace Orkeon.Tools.Email.Tests;

/// <summary>
/// Guards the layering rule of <c>Orkeon.Tools.Email</c> (ADR-005, ADR-012): Domain-side
/// assemblies, the tool abstractions, the RAG prompt-injection detector and the shared constants
/// only; never <c>Orkeon.Application</c> or <c>Orkeon.Infrastructure</c> directly. Written as an
/// allowlist, so the next reference costs a deliberate edit here.
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
        typeof(EmailToolsServiceCollectionExtensions).Assembly.GetReferencedAssemblies();

    [Fact]
    public void ToolsEmail_ReferencesNothingOutsideTheAllowlist()
    {
        var offenders = Referenced
            .Select(a => a.Name ?? string.Empty)
            .Where(name => name.StartsWith("Orkeon.", StringComparison.Ordinal))
            .Where(name => !AllowedOrkeonReferences.Contains(name, StringComparer.Ordinal))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Orkeon.Tools.Email references " + string.Join(", ", offenders)
            + " — outside the layering allowlist. Add it deliberately, or route through an abstraction.");
    }

    /// <summary>
    /// The two the allowlist exists to keep out, named explicitly: the compiler prunes unused
    /// references from metadata, so an allowlist alone would pass for the wrong reason.
    /// </summary>
    [Theory]
    [InlineData("Orkeon.Application")]
    [InlineData("Orkeon.Infrastructure")]
    public void ToolsEmail_ShouldNotReference(string forbidden)
    {
        Assert.DoesNotContain(Referenced, a => a.Name == forbidden);
    }

    [Theory]
    [InlineData("Orkeon.Domain")]
    [InlineData("Orkeon.Tools.Abstractions")]
    [InlineData("Orkeon.Rag")]
    [InlineData("MailKit")]
    [InlineData("MimeKit")]
    public void ToolsEmail_ShouldReference(string required)
    {
        Assert.Contains(Referenced, a => a.Name == required);
    }

    [Fact]
    public void ToolsEmail_ShouldKeepItsInternalsInternal()
    {
        var exported = typeof(EmailToolsServiceCollectionExtensions).Assembly.GetExportedTypes()
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
        [
            "Orkeon.Tools.Email.Administration.EmailAccountAdministration",
            "Orkeon.Tools.Email.Administration.EmailAccountStatus",
            "Orkeon.Tools.Email.Administration.EmailCheckResult",
            "Orkeon.Tools.Email.Administration.IEmailLoginInteraction",
            "Orkeon.Tools.Email.Auth.EmailTokenSet",
            "Orkeon.Tools.Email.Auth.FileSystemEmailTokenStore",
            "Orkeon.Tools.Email.Auth.IEmailTokenStore",
            "Orkeon.Tools.Email.Configuration.EmailCredentialsLocation",
            "Orkeon.Tools.Email.DependencyInjection.EmailToolsServiceCollectionExtensions",
            "Orkeon.Tools.Email.EmailErrorCode",
            "Orkeon.Tools.Email.EmailToolException",
        ], exported);
    }
}
