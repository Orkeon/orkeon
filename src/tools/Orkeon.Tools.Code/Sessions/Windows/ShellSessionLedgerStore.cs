using System.Diagnostics.CodeAnalysis;
using Orkeon.Compliance.Vfs;

namespace Orkeon.Tools.Code.Sessions.Windows;

/// <summary>
/// The folder of the AppContainer session ledgers, one JSON file per session:
/// <c>%LOCALAPPDATA%\Orkeon\shell-sessions\&lt;profile&gt;.json</c> by default.
/// </summary>
/// <remarks>
/// Physical I/O on purpose: the ledger is host state of the confinement backend, outside every mount
/// an agent sees, and it must be readable by the next host process before any VFS exists (the sweep of
/// a crashed session's leftovers). No agent path ever reaches it.
/// </remarks>
[SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: host bookkeeping of the shell confinement (the ACEs and AppContainer profiles a session leaves on the host), read back by the next host process before any VFS exists; never an agent path.")]
internal sealed class ShellSessionLedgerStore
{
    /// <summary>A store over <paramref name="directory"/>.</summary>
    /// <param name="directory">The folder; created at the first write.</param>
    public ShellSessionLedgerStore(string directory) => Directory = directory;

    /// <summary>The folder of the ledgers.</summary>
    public string Directory { get; }

    /// <summary>The store of the current user (<c>%LOCALAPPDATA%\Orkeon\shell-sessions</c>).</summary>
    public static ShellSessionLedgerStore ForCurrentUser() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "Orkeon", "shell-sessions"));

    /// <summary>Writes (or replaces) the ledger of a session.</summary>
    public void Write(ShellSessionLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        System.IO.Directory.CreateDirectory(Directory);
        var path = PathOf(ledger.ProfileName);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, ledger.Serialize());
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Deletes the ledger of a session. Never throws.</summary>
    [SuppressMessage("Design", "CA1031", Justification = "Best-effort cleanup: a ledger left behind is swept by the next host.")]
    public void Delete(string profileName)
    {
        try
        {
            File.Delete(PathOf(profileName));
        }
        catch
        {
            // Left for the next sweep.
        }
    }

    /// <summary>Every ledger of the folder, with the profile its file is named after (null for an unreadable one).</summary>
    [SuppressMessage("Design", "CA1031", Justification = "A ledger being written or locked is skipped; the next sweep reads it.")]
    public IReadOnlyList<(string ProfileName, ShellSessionLedger? Ledger)> ReadAll()
    {
        if (!System.IO.Directory.Exists(Directory))
            return [];

        var ledgers = new List<(string, ShellSessionLedger?)>();
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!name.StartsWith(AppContainerShellSandbox.ProfileNamePrefix, StringComparison.Ordinal))
                continue;
            try
            {
                ledgers.Add((name, ShellSessionLedger.Deserialize(File.ReadAllText(file))));
            }
            catch
            {
                // Unreadable now: skipped.
            }
        }

        return ledgers;
    }

    private string PathOf(string profileName) => Path.Combine(Directory, profileName + ".json");
}
