# Orkeon.Compliance.Vfs

A Roslyn analyzer that refuses direct `System.IO` in your own code — the guard rail of
[Orkeon](https://github.com/Orkeon/orkeon), usable in any C# project. Seven diagnostics
(`ORKVFS001`–`ORKVFS007`), errors by default, no runtime dependency, nothing to configure.

Point of it: an AI coding agent — or a teammate in a hurry — writes `File.WriteAllText`
where the design said "through the file-system abstraction". With this analyzer in the
project, that line does not compile.

## Install

```xml
<PackageReference Include="Orkeon.Compliance.Vfs" Version="1.0.0-rc.4" PrivateAssets="all" />
```

`dotnet build` — and every `File.*`, `Directory.*`, `new FileStream(...)`, `new FileSystemWatcher(...)`,
`new StreamReader("<path>")` and `Path.GetFullPath(<user input>)` is an `ORKVFS` error.
Nothing else from Orkeon is required: the package depends on no Orkeon assembly.

> `1.0.0-rc.3` of this package was inert outside the Orkeon repository: it was compiled
> against Roslyn 5.9.0, newer than the compiler of a stock .NET 10 SDK, and the compiler
> skipped it with `CS9057`. Since `1.0.0-rc.4` it is compiled against Roslyn 4.8.0 — any SDK
> from .NET 8.0.100 on loads it.

| Rule | Reports |
|---|---|
| `ORKVFS001` | a call on `System.IO.File` |
| `ORKVFS002` | a call on `System.IO.Directory` |
| `ORKVFS003` | `new` of a `System.IO` file-system type (`FileStream`, `FileInfo`, `DirectoryInfo`…) |
| `ORKVFS004` | `Path.GetFullPath` on a string — the call that turns user input into a real path without any mount check |
| `ORKVFS005` | `new FileSystemWatcher(...)` |
| `ORKVFS006` | `new StreamReader` / `new StreamWriter` on a path string |
| `ORKVFS007` | a nullable `IFileSystemService?` field or parameter (only meaningful when you use Orkeon's abstraction) |

## What is exempt, and how to opt out

- **Paths.** Files whose path contains `/tests/` or `/examples/` are not analysed, so test
  suites and samples may touch the real disk. (Orkeon's own VFS implementation folders are
  exempt by the same mechanism.)
- **Per symbol.** Put an attribute named exactly `Orkeon.Compliance.Vfs.SuppressVfsComplianceAttribute`
  on the method, type, member — or the assembly. The analyzer matches it by name, so declare
  it yourself, once, `internal`, in any file of the project:

  ```csharp
  namespace Orkeon.Compliance.Vfs;

  [System.AttributeUsage(System.AttributeTargets.All, Inherited = false)]
  internal sealed class SuppressVfsComplianceAttribute(string reason) : System.Attribute
  {
      public string Reason { get; } = reason;
  }
  ```

  The `reason` is mandatory by design: a bare exemption is the thing this analyzer exists
  to prevent.
- **Severity.** Standard Roslyn configuration — `.editorconfig`:

  ```ini
  dotnet_diagnostic.ORKVFS001.severity = warning
  dotnet_diagnostic.ORKVFS007.severity = none   # you do not use IFileSystemService
  ```

## Documentation

- [VFS compliance — the rules, one by one](https://github.com/Orkeon/orkeon/blob/main/docs/architecture/vfs-compliance.md)
- [Publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md) — what ships where
- [Verify what you install](https://github.com/Orkeon/orkeon/blob/main/docs/guides/verify-what-you-install.md) — provenance attestation for this package

MIT © Orkeon Contributors
