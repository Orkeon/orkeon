; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 1.0.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
ORKVFS001 | Orkeon.Vfs | Error | Direct System.IO.File usage
ORKVFS002 | Orkeon.Vfs | Error | Direct System.IO.Directory usage
ORKVFS003 | Orkeon.Vfs | Error | Direct FileStream/FileInfo/DirectoryInfo instantiation with a path
ORKVFS004 | Orkeon.Vfs | Error | Path.GetFullPath on potentially user-supplied input
ORKVFS005 | Orkeon.Vfs | Error | Direct FileSystemWatcher instantiation
ORKVFS006 | Orkeon.Vfs | Error | Direct StreamReader/StreamWriter instantiation with a string path
ORKVFS007 | Orkeon.Vfs | Error | Nullable IFileSystemService field/parameter in framework code
