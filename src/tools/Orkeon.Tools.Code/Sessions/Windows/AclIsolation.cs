using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Orkeon.Compliance.Vfs;

namespace Orkeon.Tools.Code.Sessions.Windows;

/// <summary>
/// Cuts the paths of <see cref="AclPlan.Isolations"/> off the ACEs their parents pass down to the
/// session's AppContainer SID, and puts their DACL back when the session ends.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not a deny ACE, nor an integrity label.</b> The access check of an AppContainer process
/// keeps only what the allow ACEs grant its package SID: a hidden sub path below a read-write mount
/// was still read from inside the container with a deny-everything ACE for the SID ahead of the
/// inherited allow, and again with an explicit Medium no-read-up label on it (seen on
/// windows-latest: a DACL grant to the package SID passes the mandatory integrity check too). The
/// only refusal that holds is the absence of an allow: the path's DACL is protected (it stops
/// inheriting), the ACEs it inherited are kept as explicit copies, minus those of the session's SID.
/// </para>
/// <para>
/// The DACL the path had is recorded in the ledger first and written back as it was (inheritance
/// restored) when the session ends; a change another tool made to that DACL meanwhile is lost. A path
/// another session of the store also isolated is put back by the last of them only.
/// </para>
/// <para>
/// Protecting a DACL needs <c>WRITE_DAC</c> on the path; a path the host may not change makes the
/// session fail to start rather than run without the refusal.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
[SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: provisions, below the VFS, the OS-level view of a confined shell session: the DACLs that keep the AppContainer out of the paths the VFS hides or keeps read-only. Not a read or write of a file's content.")]
internal static class AclIsolation
{
    /// <summary>Serialises the isolation decisions of the sessions of this process with their ledgers.</summary>
    public static readonly Lock Sync = new();

    /// <summary>The DACL of <paramref name="path"/> as SDDL, or null when the path does not exist.</summary>
    public static string? Read(string path)
        => SecurityOf(path)?.GetSecurityDescriptorSddlForm(AccessControlSections.Access);

    /// <summary>Whether the DACL of <paramref name="path"/> is protected from inheritance (the tests read it).</summary>
    public static bool IsProtected(string path) => SecurityOf(path)?.AreAccessRulesProtected ?? false;

    /// <summary>
    /// Protects the DACL of each path, keeping what it inherited as explicit ACEs except those of
    /// <paramref name="sid"/>; a path that no longer exists is skipped.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">The host may not change the DACL of a path.</exception>
    public static void Apply(IEnumerable<string> paths, SecurityIdentifier sid)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(sid);
        foreach (var path in paths)
        {
            if (SecurityOf(path) is not { } security)
                continue;
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: true);
            security.PurgeAccessRules(sid);
            Write(path, security);
        }
    }

    /// <summary>Writes back the DACL a path had (<paramref name="originalSddl"/>). Never throws.</summary>
    /// <returns>False when it could not be written (the ledger keeps it for the next sweep).</returns>
    [SuppressMessage("Design", "CA1031", Justification = "Best-effort cleanup: a path locked or out of reach is reported to the caller, which keeps it in the ledger.")]
    public static bool Restore(string path, string originalSddl)
    {
        try
        {
            var security = EmptySecurityOf(path);
            if (security is null)
                return true;
            security.SetSecurityDescriptorSddlForm(originalSddl, AccessControlSections.Access);
            Write(path, security);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static FileSystemSecurity? EmptySecurityOf(string path)
    {
        if (Directory.Exists(path))
            return new DirectorySecurity();
        if (File.Exists(path))
            return new FileSecurity();
        return null;
    }

    private static FileSystemSecurity? SecurityOf(string path)
    {
        if (Directory.Exists(path))
            return new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access);
        if (File.Exists(path))
            return new FileInfo(path).GetAccessControl(AccessControlSections.Access);
        return null;
    }

    private static void Write(string path, FileSystemSecurity security)
    {
        if (security is DirectorySecurity directory)
            new DirectoryInfo(path).SetAccessControl(directory);
        else
            new FileInfo(path).SetAccessControl((FileSecurity)security);
    }
}
