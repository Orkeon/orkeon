using Microsoft.CodeAnalysis;

namespace Orkeon.Compliance.Vfs;

internal static class DiagnosticDescriptors
{
    private const string Category = "Orkeon.Vfs";
    private const string HelpLinkBase = "https://github.com/Orkeon/orkeon/blob/main/docs/architecture/vfs-compliance.md#";

    public static readonly DiagnosticDescriptor DirectFileUsage = new(
        id: "ORKVFS001",
        title: "Direct System.IO.File usage is forbidden in framework code",
        messageFormat: "Direct call to 'System.IO.File.{0}' — route filesystem access through IFileSystemService",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Framework code must use IFileSystemService for all filesystem access to respect mount boundaries, access rights, and path validation.",
        helpLinkUri: HelpLinkBase + "ork-vfs-001");

    public static readonly DiagnosticDescriptor DirectDirectoryUsage = new(
        id: "ORKVFS002",
        title: "Direct System.IO.Directory usage is forbidden in framework code",
        messageFormat: "Direct call to 'System.IO.Directory.{0}' — route filesystem access through IFileSystemService",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Framework code must use IFileSystemService for all directory operations.",
        helpLinkUri: HelpLinkBase + "ork-vfs-002");

    public static readonly DiagnosticDescriptor DirectFileSystemTypeInstantiation = new(
        id: "ORKVFS003",
        title: "Direct instantiation of file-system types is forbidden",
        messageFormat: "Direct instantiation of '{0}' with a path — use IFileSystemService streams/entries instead",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "FileStream/FileInfo/DirectoryInfo constructed from a path bypass mount boundaries. Use IFileSystemService.OpenReadStreamAsync / OpenWriteStreamAsync / TryGetEntryAsync.",
        helpLinkUri: HelpLinkBase + "ork-vfs-003");

    public static readonly DiagnosticDescriptor SuspiciousPathGetFullPath = new(
        id: "ORKVFS004",
        title: "Path.GetFullPath on user-supplied input bypasses mount validation",
        messageFormat: "Path.GetFullPath on potentially user-supplied input — prefer IFileSystemService.ResolveAndValidate",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Path.GetFullPath does not validate mount boundaries or access rights. For user input, use IFileSystemService.ResolveAndValidate.",
        helpLinkUri: HelpLinkBase + "ork-vfs-004");

    public static readonly DiagnosticDescriptor DirectFileSystemWatcher = new(
        id: "ORKVFS005",
        title: "Direct FileSystemWatcher instantiation is forbidden",
        messageFormat: "Direct instantiation of 'FileSystemWatcher' — use IVirtualFileSystemWatcher via IFileSystemService",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "FileSystemWatcher bypasses mount boundaries. Use IFileSystemService's watcher abstraction.",
        helpLinkUri: HelpLinkBase + "ork-vfs-005");

    public static readonly DiagnosticDescriptor DirectStreamReaderWriterPath = new(
        id: "ORKVFS006",
        title: "Direct StreamReader/StreamWriter on a file path is forbidden",
        messageFormat: "Direct instantiation of '{0}' with a string path — open the file via IFileSystemService.OpenReadStreamAsync / OpenWriteStreamAsync and wrap the returned Stream",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "new StreamReader(string)/new StreamWriter(string) open a file by path, bypassing mount boundaries and access rights. Wrap a Stream obtained from IFileSystemService instead.",
        helpLinkUri: HelpLinkBase + "ork-vfs-006");

    public static readonly DiagnosticDescriptor NullableFileSystemService = new(
        id: "ORKVFS007",
        title: "Nullable IFileSystemService is forbidden in framework code",
        messageFormat: "'IFileSystemService?' is nullable here — declare it as a required (non-nullable) dependency so no System.IO fallback path can be reintroduced",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A nullable IFileSystemService re-opens the door to a 'if (_fs is null) { …System.IO… }' fallback. Inject IFileSystemService as a required, non-nullable dependency.",
        helpLinkUri: HelpLinkBase + "ork-vfs-007");
}
