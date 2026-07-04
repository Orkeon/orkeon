using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Application.Interfaces.Ports
{
    /// <summary>
    /// Simple LLM provider interface for basic chat functionality
    /// </summary>
    public interface IBasicLlmProvider
    {
        /// <summary>
        /// Send a chat message to the LLM and get a response
        /// </summary>
        System.Threading.Tasks.Task<string> ChatAsync(
            string message,
            LlmConfig? config = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Provider name
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Check if the provider is available
        /// </summary>
        System.Threading.Tasks.Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
    }
}
