using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace Orkeon.Tools.Code.Sessions.Windows;

/// <summary>
/// The AppContainer profile of one shell session: a name unique to the session and the SID the
/// kernel derives from it. A process started with that SID and no capability reaches only what an ACE
/// grants the SID (or <c>ALL APPLICATION PACKAGES</c>: the Windows and Program Files trees), and has
/// no network: the network is a capability (<c>internetClient</c>, <c>privateNetworkClientServer</c>)
/// it is given only when the view allows it.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class AppContainerProfile
{
    /// <summary>The prefix of every profile a session creates; the sweep only ever touches these.</summary>
    public const string NamePrefix = AppContainerShellSandbox.ProfileNamePrefix;

    /// <summary><c>internetClient</c>: outbound connections to the internet.</summary>
    public static readonly SecurityIdentifier InternetClient = new("S-1-15-3-1");

    /// <summary><c>privateNetworkClientServer</c>: the home and work networks, both ways.</summary>
    public static readonly SecurityIdentifier PrivateNetworkClientServer = new("S-1-15-3-3");

    private AppContainerProfile(string name, SecurityIdentifier sid)
    {
        Name = name;
        Sid = sid;
    }

    /// <summary>The profile's name (<c>Orkeon.Shell.&lt;guid&gt;</c>).</summary>
    public string Name { get; }

    /// <summary>The AppContainer SID (<c>S-1-15-2-…</c>) the ACEs and the process token carry.</summary>
    public SecurityIdentifier Sid { get; }

    /// <summary>A profile name no other session holds.</summary>
    public static string NewName() => NamePrefix + Guid.NewGuid().ToString("N");

    /// <summary>
    /// Creates the profile <paramref name="name"/>, or takes the SID of the existing one (a profile a
    /// crash left behind under the same name).
    /// </summary>
    /// <exception cref="Win32Exception">The profile could not be created nor its SID derived.</exception>
    public static AppContainerProfile Create(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var hr = NativeMethods.CreateAppContainerProfile(name, name, "Orkeon shell session", 0, 0, out var sid);
        if (hr == NativeMethods.HResultAlreadyExists)
            hr = NativeMethods.DeriveAppContainerSidFromAppContainerName(name, out sid);
        if (hr < 0)
            throw new Win32Exception(hr, $"CreateAppContainerProfile failed (0x{hr:X8})");

        try
        {
            return new AppContainerProfile(name, new SecurityIdentifier(sid));
        }
        finally
        {
            NativeMethods.FreeSid(sid);
        }
    }

    /// <summary>
    /// The profile's own folder (<c>%LOCALAPPDATA%\Packages\&lt;name&gt;\AC</c>), the one place the
    /// container may write without an ACE; null when Windows does not give it.
    /// </summary>
    public string? FolderPath()
    {
        if (NativeMethods.GetAppContainerFolderPath(Sid.Value, out var path) < 0 || path == 0)
            return null;
        try
        {
            return Marshal.PtrToStringUni(path);
        }
        finally
        {
            Marshal.FreeCoTaskMem(path);
        }
    }

    /// <summary>Deletes the profile <paramref name="name"/> and its folder. Never throws.</summary>
    /// <returns>Whether Windows deleted it (or it did not exist).</returns>
    public static bool Delete(string name)
    {
        // DeleteAppContainerProfile answers S_OK for a profile that does not exist.
        return NativeMethods.DeleteAppContainerProfile(name) >= 0;
    }
}
