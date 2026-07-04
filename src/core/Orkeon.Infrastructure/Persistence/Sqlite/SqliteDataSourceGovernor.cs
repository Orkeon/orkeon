using Microsoft.Data.Sqlite;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;

namespace Orkeon.Infrastructure.Persistence.Sqlite;

/// <summary>
/// Routes a SQLite connection string's <c>Data Source</c> file path through the
/// virtual file system (VFS) so the on-disk database lives under a validated,
/// mounted, writable location (lot VFS-70, decision 2F-A).
/// </summary>
/// <remarks>
/// The SQLite engine requires a real physical path (or <c>:memory:</c>) and cannot
/// operate over the VFS streams, so governance happens at construction time:
/// the virtual <c>Data Source</c> is resolved + validated via
/// <see cref="IFileSystemService.ResolveAndValidate"/> (Read|Write|Create) and the
/// connection string is rebuilt with the validated physical path before it reaches
/// <c>Microsoft.Data.Sqlite</c>. In-memory databases (<c>:memory:</c> or
/// <c>Mode=Memory</c>) never touch disk and pass through untouched.
/// </remarks>
internal static class SqliteDataSourceGovernor
{
    /// <summary>
    /// Returns a connection string whose file <c>Data Source</c> has been resolved and
    /// validated through the VFS. In-memory connection strings are returned unchanged.
    /// </summary>
    /// <param name="connectionString">The raw SQLite connection string (virtual <c>Data Source</c>).</param>
    /// <param name="fileSystem">The virtual file system used to resolve and validate the path.</param>
    /// <exception cref="FileAccessDeniedException">
    /// Thrown when the file data source does not resolve to a writable VFS mount.
    /// </exception>
    public static string Govern(string connectionString, IFileSystemService fileSystem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(fileSystem);

        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource;

        // In-memory databases never touch disk → passthrough untouched.
        if (builder.Mode == SqliteOpenMode.Memory
            || string.IsNullOrEmpty(dataSource)
            || dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return connectionString;
        }

        var resolution = fileSystem.ResolveAndValidate(
            dataSource,
            FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create);

        if (!resolution.IsAllowed || resolution.ResolvedPath is null)
        {
            throw new FileAccessDeniedException(
                $"SQLite data source '{dataSource}' is not within a writable VFS mount: " +
                $"{resolution.DenialReason ?? "access denied"}.",
                dataSource,
                FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create);
        }

        builder.DataSource = resolution.ResolvedPath;
        return builder.ConnectionString;
    }
}
