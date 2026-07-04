using Orkeon.Domain.Configuration;

namespace Orkeon.Application.Interfaces.Infrastructure
{
    /// <summary>
    /// Service for comparing YAML configurations and generating diffs
    /// </summary>
    public interface IYamlDiffService
    {
        /// <summary>
        /// Compare two YAML strings and generate a diff
        /// </summary>
        System.Threading.Tasks.Task<ConfigurationDiff> CompareYamlAsync(
            string yaml1,
            string yaml2,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Apply a diff to a base YAML content
        /// </summary>
        System.Threading.Tasks.Task<string> ApplyDiffAsync(
            string baseContent,
            ConfigurationDiff diff,
            CancellationToken cancellationToken = default);
    }
}
