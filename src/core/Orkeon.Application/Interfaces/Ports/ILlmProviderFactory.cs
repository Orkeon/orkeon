using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Factory interface for creating LLM providers.
/// This allows the Infrastructure layer to provide implementations
/// while maintaining proper architectural boundaries.
/// </summary>
public interface ILlmProviderFactory
{
    /// <summary>
    /// Creates an LLM provider based on configuration.
    /// </summary>
    /// <param name="config">The LLM configuration</param>
    /// <returns>An LLM provider instance</returns>
    IBasicLlmProvider Create(LlmConfig config);
}
