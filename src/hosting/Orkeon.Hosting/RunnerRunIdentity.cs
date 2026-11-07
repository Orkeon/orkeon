using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Services;

namespace Orkeon.Hosting;

/// <summary>
/// What the host tells the orchestrator about the run it is about to start (FWK-01, FWK-07): the
/// identifier a command gave its run before the kickoff (<c>orkeon run --events</c> opens the run's
/// <see cref="IRunScope"/> before <c>run.started</c>, so that every event of the stream carries one
/// <c>correlationId</c>, which the kickoff must adopt rather than mint a second one), the stable key
/// of the crew, and the run to resume.
/// </summary>
internal static class RunnerRunIdentity
{
    /// <summary>The options of a kickoff: the ambient run's identifier, the crew's key and the run to resume.</summary>
    internal static KickoffOptions For(IServiceProvider services, string? crewName, string virtualConfigPath, string physicalConfigPath, string? resume)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new KickoffOptions
        {
            ExecutionId = services.GetService<IRunScope>()?.Current?.ExecutionId,
            CrewKey = CrewKey(crewName, virtualConfigPath, physicalConfigPath),
            Resume = string.IsNullOrWhiteSpace(resume) ? null : resume,
        };
    }

    /// <summary>
    /// The key a crew's checkpoints are kept under: its name and the virtual path of its file, so that
    /// two crews of one name in different files never resume one another, and a short digest of the (already absolute)
    /// file's full path, because two folders both appear to the virtual file system as <c>/crew</c>.
    /// The digest is one way: no physical path is written to the state store. It is also why a run cannot be
    /// resumed from another location: a moved or renamed checkout is another crew key, and its earlier runs are orphaned.
    /// </summary>
    internal static string CrewKey(string? crewName, string virtualConfigPath, string physicalConfigPath)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(physicalConfigPath)))[..16];
        return $"{(string.IsNullOrWhiteSpace(crewName) ? "crew" : crewName)}@{virtualConfigPath}~{digest}";
    }
}
