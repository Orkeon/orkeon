using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Orkeon.Compliance.Vfs;

namespace Orkeon.Tools.Code.Sessions.Windows;

/// <summary>
/// Writes an <see cref="AclPlan"/> into the DACLs of the host paths, and takes it back out. The
/// managed <see cref="FileSystemAclExtensions"/> API of the shared framework does the work
/// (<c>GetNamedSecurityInfo</c> / <c>SetNamedSecurityInfo</c> underneath).
/// </summary>
/// <remarks>
/// <para>
/// <b>Cost on a big tree.</b> The ACE is written once, on the mount's root, inheritable; but
/// <c>SetNamedSecurityInfo</c> then propagates it: Windows walks every file and directory below that
/// inherits from its parent and rewrites its security descriptor. Granting a mount, and taking the
/// grant back, therefore costs one write per object of the tree (a <c>node_modules</c> of 100 000 files
/// means 100 000 descriptor writes, each way). It happens once per session, not per command. Should it
/// prove too slow, the way out is a stable AppContainer SID per set of rights, granted once and kept
/// across sessions, instead of one SID per session.
/// </para>
/// <para>
/// The DACL is read, changed and written back, which is not atomic: two sessions of this host
/// changing the same path are serialised here; another process changing it at the same moment could
/// lose one of the two changes.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
[SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: provisions, below the VFS, the OS-level view of a confined shell session: the ACEs that let the AppContainer reach the physical paths the VFS already resolved and checked. Not a read or write of a file's content.")]
internal static class AclGrants
{
    private const InheritanceFlags DirectoryInheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

    private static readonly Lock s_sync = new();

    /// <summary>Writes every entry for <paramref name="sid"/>; a path that no longer exists is skipped.</summary>
    /// <exception cref="UnauthorizedAccessException">The host may not change the DACL of a path.</exception>
    public static void Apply(IEnumerable<AclEntry> entries, SecurityIdentifier sid)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(sid);
        lock (s_sync)
        {
            foreach (var entry in entries)
                ApplyOne(entry, sid);
        }
    }

    /// <summary>Removes every ACE of <paramref name="sid"/> from each path. Never throws.</summary>
    /// <returns>The paths whose ACEs could not be removed (they stay in the ledger for the next sweep).</returns>
    [SuppressMessage("Design", "CA1031", Justification = "Best-effort cleanup: a path deleted, locked or out of reach must not stop the cleanup of the others; it is reported to the caller.")]
    public static IReadOnlyList<string> Purge(IEnumerable<string> paths, SecurityIdentifier sid)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(sid);
        var failed = new List<string>();
        lock (s_sync)
        {
            foreach (var path in paths)
            {
                try
                {
                    PurgeOne(path, sid);
                }
                catch
                {
                    failed.Add(path);
                }
            }
        }

        return failed;
    }

    /// <summary>Whether <paramref name="path"/> holds an explicit ACE of <paramref name="sid"/> (the tests read it).</summary>
    public static bool HasExplicitAce(string path, SecurityIdentifier sid)
    {
        ArgumentNullException.ThrowIfNull(sid);
        FileSystemSecurity? security = Directory.Exists(path) ? new DirectoryInfo(path).GetAccessControl()
            : File.Exists(path) ? new FileInfo(path).GetAccessControl()
            : null;
        return security is not null && security
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .OfType<FileSystemAccessRule>()
            .Any(r => sid.Equals(r.IdentityReference));
    }

    private static void ApplyOne(AclEntry entry, SecurityIdentifier sid)
    {
        var rights = RightsOf(entry.Rights);
        var type = entry.Deny ? AccessControlType.Deny : AccessControlType.Allow;
        if (Directory.Exists(entry.Path))
        {
            var directory = new DirectoryInfo(entry.Path);
            var security = directory.GetAccessControl(AccessControlSections.Access);
            security.AddAccessRule(new FileSystemAccessRule(sid, rights, DirectoryInheritance, PropagationFlags.None, type));
            directory.SetAccessControl(security);
        }
        else if (File.Exists(entry.Path))
        {
            var file = new FileInfo(entry.Path);
            var security = file.GetAccessControl(AccessControlSections.Access);
            security.AddAccessRule(new FileSystemAccessRule(sid, rights, type));
            file.SetAccessControl(security);
        }
    }

    private static void PurgeOne(string path, SecurityIdentifier sid)
    {
        if (Directory.Exists(path))
        {
            var directory = new DirectoryInfo(path);
            var security = directory.GetAccessControl(AccessControlSections.Access);
            security.PurgeAccessRules(sid);
            directory.SetAccessControl(security);
        }
        else if (File.Exists(path))
        {
            var file = new FileInfo(path);
            var security = file.GetAccessControl(AccessControlSections.Access);
            security.PurgeAccessRules(sid);
            file.SetAccessControl(security);
        }
    }

    private static FileSystemRights RightsOf(AclRights rights) => rights switch
    {
        AclRights.ReadAndExecute => FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize,
        AclRights.Modify => FileSystemRights.Modify | FileSystemRights.Synchronize,
        AclRights.Write => FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles
                           | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership,
        AclRights.All => FileSystemRights.FullControl,
        _ => throw new ArgumentOutOfRangeException(nameof(rights), rights, "Unknown ACL rights."),
    };
}
