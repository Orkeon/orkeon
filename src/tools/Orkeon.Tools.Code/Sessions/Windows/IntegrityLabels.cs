using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Orkeon.Compliance.Vfs;

namespace Orkeon.Tools.Code.Sessions.Windows;

/// <summary>
/// Writes the integrity labels of an <see cref="AclPlan"/> (<see cref="AclPlan.Labels"/>) on the host
/// paths, and puts the labels they had back. An AppContainer runs at Low integrity: an explicit
/// Medium label with a no-read-up or no-write-up policy refuses it what the policy names, whatever
/// the DACL grants (the mandatory integrity check comes before the DACL).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why labels and not deny ACEs.</b> The access check of an AppContainer process does not stop at
/// an ACE that denies its package SID: a hidden sub path that carried a deny-everything ACE for the
/// session's SID, ahead of the allow its read-write mount passed down, was still read from inside the
/// container (seen on windows-latest). The deny ACEs of the plan stay as a second layer; the label is
/// what refuses.
/// </para>
/// <para>
/// A label applies to every process below its level, not only to the session: while the session
/// lives, a Low-integrity process of the host's user (a protected-mode viewer) is refused the same
/// paths. Processes at Medium (the user's) or High (elevated) are not affected.
/// </para>
/// <para>
/// Setting a label needs <c>WRITE_OWNER</c> on the path; a path the host may not relabel makes the
/// session fail to start rather than run without the refusal.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
[SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: provisions, below the VFS, the OS-level view of a confined shell session: the integrity labels that refuse the AppContainer the paths the VFS hides or keeps read-only. Not a read or write of a file's content.")]
internal static class IntegrityLabels
{
    /// <summary>Serialises the label decisions of the sessions of this process with their ledgers.</summary>
    public static readonly Lock Sync = new();

    /// <summary>The explicit label of <paramref name="path"/> (empty when none), or null when the path does not exist.</summary>
    /// <exception cref="UnauthorizedAccessException">The host may not read the path's label.</exception>
    public static string? Read(string path)
    {
        var error = NativeMethods.GetNamedSecurityInfo(path, NativeMethods.SeFileObject, NativeMethods.LabelSecurityInformation, 0, 0, 0, 0, out var descriptor);
        if (error is NativeMethods.ErrorFileNotFound or NativeMethods.ErrorPathNotFound)
            return null;
        ThrowOnError(error, path);
        try
        {
            if (!NativeMethods.ConvertSecurityDescriptorToStringSecurityDescriptor(descriptor, NativeMethods.SddlRevision1, NativeMethods.LabelSecurityInformation, out var text, 0))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            try
            {
                return LabelSddl.ExplicitOf(Marshal.PtrToStringUni(text) ?? "");
            }
            finally
            {
                NativeMethods.LocalFree(text);
            }
        }
        finally
        {
            NativeMethods.LocalFree(descriptor);
        }
    }

    /// <summary>Writes each label; a path that no longer exists is skipped.</summary>
    /// <exception cref="UnauthorizedAccessException">The host may not relabel a path.</exception>
    public static void Apply(IEnumerable<LabelEntry> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        foreach (var label in labels)
        {
            var directory = Directory.Exists(label.Path);
            if (!directory && !File.Exists(label.Path))
                continue;
            Write(label.Path, LabelSddl.Of(label.Policy, directory));
        }
    }

    /// <summary>Puts back the explicit label a path had (<paramref name="original"/>, empty: none). Never throws.</summary>
    /// <returns>False when the label could not be put back (the ledger keeps it for the next sweep).</returns>
    [SuppressMessage("Design", "CA1031", Justification = "Best-effort cleanup: a path locked or out of reach is reported to the caller, which keeps it in the ledger.")]
    public static bool Restore(string path, string original)
    {
        try
        {
            if (!Directory.Exists(path) && !File.Exists(path))
                return true;
            if (string.IsNullOrEmpty(original))
                RemoveExplicit(path);
            else
                Write(path, original);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void Write(string path, string sddl)
    {
        if (!NativeMethods.ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, NativeMethods.SddlRevision1, out var descriptor, 0))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Invalid label '{sddl}'.");
        try
        {
            if (!NativeMethods.GetSecurityDescriptorSacl(descriptor, out var present, out var sacl, out _) || !present)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), $"The label '{sddl}' holds no SACL.");
            ThrowOnError(NativeMethods.SetNamedSecurityInfo(path, NativeMethods.SeFileObject, NativeMethods.LabelSecurityInformation, 0, 0, 0, sacl), path);
        }
        finally
        {
            NativeMethods.LocalFree(descriptor);
        }
    }

    /// <summary>Removes the explicit label: an empty label ACL, so the path inherits again (or has none).</summary>
    private static unsafe void RemoveExplicit(string path)
    {
        // An ACL header alone (8 bytes, no ACE), 4-byte aligned.
        var acl = stackalloc long[1];
        if (!NativeMethods.InitializeAcl(acl, sizeof(long), NativeMethods.AclRevision))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        ThrowOnError(NativeMethods.SetNamedSecurityInfo(path, NativeMethods.SeFileObject, NativeMethods.LabelSecurityInformation, 0, 0, 0, (nint)acl), path);
    }

    private static void ThrowOnError(int error, string path)
    {
        if (error == 0)
            return;
        if (error == NativeMethods.ErrorAccessDenied)
            throw new UnauthorizedAccessException($"The integrity label of '{path}' cannot be changed by this host (access denied).");
        throw new Win32Exception(error);
    }
}
