using Orkeon.Application.Services.Security;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Configuration options for the guardian system, bound from <c>Orkeon:Guardian</c>.
/// The guardian is on by default: it blocks High and Critical findings only and never
/// rewrites content. A block or a warning is logged and written to the audit trail
/// (<c>Security:Audit</c>).
/// </summary>
public class GuardianOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the guardian runs. When false no guardian is
    /// registered in the pipeline: agent turns are neither screened on input nor on tool calls.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the default guardian policy (which phases run, maximum delegation depth).</summary>
    public GuardianPolicy DefaultPolicy { get; set; } = new();
}
