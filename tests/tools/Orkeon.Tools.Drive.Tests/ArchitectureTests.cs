using Orkeon.Tools.Drive.DependencyInjection;

namespace Orkeon.Tools.Drive.Tests;

/// <summary>
/// Guards the layering rule of <c>Orkeon.Tools.Drive</c> (ADR-005, ADR-013): Domain-side
/// assemblies, the tool abstractions, the RAG prompt-injection detector and the shared constants
/// only; never <c>Orkeon.Application</c>, <c>Orkeon.Infrastructure</c> or the e-mail family.
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
        typeof(DriveToolsServiceCollectionExtensions).Assembly.GetReferencedAssemblies();

    [Fact]
    public void ToolsDrive_ReferencesNothingOutsideTheAllowlist()
    {
        var offenders = Referenced
            .Select(a => a.Name ?? string.Empty)
            .Where(name => name.StartsWith("Orkeon.", StringComparison.Ordinal))
            .Where(name => !AllowedOrkeonReferences.Contains(name, StringComparer.Ordinal))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Orkeon.Tools.Drive references " + string.Join(", ", offenders)
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
    [InlineData("MailKit")]
    public void ToolsDrive_ShouldNotReference(string forbidden)
    {
        Assert.DoesNotContain(Referenced, a => a.Name == forbidden);
    }

    [Theory]
    [InlineData("Orkeon.Domain")]
    [InlineData("Orkeon.Tools.Abstractions")]
    [InlineData("Orkeon.Rag")]
    public void ToolsDrive_ShouldReference(string required)
    {
        Assert.Contains(Referenced, a => a.Name == required);
    }

    [Fact]
    public void ToolsDrive_ShouldKeepItsInternalsInternal()
    {
        var exported = typeof(DriveToolsServiceCollectionExtensions).Assembly.GetExportedTypes()
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
        [
            "Orkeon.Tools.Drive.Accounts.DriveRightsCheck",
            "Orkeon.Tools.Drive.Administration.DriveAccountAdministration",
            "Orkeon.Tools.Drive.Administration.DriveAccountStatus",
            "Orkeon.Tools.Drive.Administration.DriveCheckResult",
            "Orkeon.Tools.Drive.Auth.DriveTokenSet",
            "Orkeon.Tools.Drive.Auth.FileSystemDriveTokenStore",
            "Orkeon.Tools.Drive.Auth.IDriveTokenStore",
            "Orkeon.Tools.Drive.Configuration.DriveCredentialsLocation",
            "Orkeon.Tools.Drive.DependencyInjection.DriveToolsServiceCollectionExtensions",
            "Orkeon.Tools.Drive.DriveErrorCode",
            "Orkeon.Tools.Drive.DriveToolException",
        ], exported);
    }
}
